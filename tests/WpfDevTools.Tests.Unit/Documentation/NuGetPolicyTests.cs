using System.Xml.Linq;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class NuGetPolicyTests
{
    private static readonly string[] ProjectFiles =
    [
        "src/WpfDevTools.Shared/WpfDevTools.Shared.csproj",
        "src/WpfDevTools.Inspector/WpfDevTools.Inspector.csproj",
        "src/WpfDevTools.Injector/WpfDevTools.Injector.csproj",
        "src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj",
        "src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj",
        "tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj",
        "tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj",
        "tests/WpfDevTools.Tests.TestApp/WpfDevTools.Tests.TestApp.csproj"
    ];

    [Fact]
    public void DirectoryPackagesProps_ShouldEnableCentralPackageManagement()
    {
        var document = LoadXml("Directory.Packages.props");

        document.Descendants("ManagePackageVersionsCentrally")
            .Single()
            .Value
            .Should()
            .Be("true");
    }

    [Fact]
    public void PackageReferences_ShouldNotDeclareInlineVersions()
    {
        foreach (var projectPath in ProjectFiles)
        {
            var references = LoadXml(projectPath)
                .Descendants("PackageReference")
                .Where(element => element.Attribute("Include") is not null)
                .ToArray();

            if (references.Length == 0)
            {
                continue;
            }

            references.Should().OnlyContain(
                reference => reference.Attribute("Version") == null
                    && reference.Attribute("VersionOverride") == null,
                $"{projectPath} should use Directory.Packages.props for package versions");
        }
    }

    [Fact]
    public void DirectoryPackagesProps_ShouldDefineAllReferencedPackageVersions()
    {
        var centralPackageIds = LoadXml("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referencedPackageIds = ProjectFiles
            .SelectMany(projectPath => LoadXml(projectPath).Descendants("PackageReference"))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        referencedPackageIds.Should().OnlyContain(
            packageId => centralPackageIds.Contains(packageId!),
            "every PackageReference should have a central PackageVersion entry");
    }

    [Fact]
    public void NuGetConfig_ShouldRestrictPackagesWithSourceMapping()
    {
        var document = LoadXml("NuGet.config");

        document.Descendants("packageSources")
            .Single()
            .Elements("clear")
            .Should()
            .ContainSingle();

        var nugetSource = document.Descendants("packageSources")
            .Single()
            .Elements("add")
            .Single(element => element.Attribute("key")?.Value == "nuget.org");

        nugetSource.Attribute("value")?.Value.Should().Be("https://api.nuget.org/v3/index.json");

        var mappedPackages = document.Descendants("packageSourceMapping")
            .Single()
            .Elements("packageSource")
            .Single(element => element.Attribute("key")?.Value == "nuget.org")
            .Elements("package")
            .Select(element => element.Attribute("pattern")?.Value)
            .ToArray();

        mappedPackages.Should().Contain("*");
    }

    [Fact]
    public void DirectoryBuildProps_ShouldEnableLockFilesWithoutForcingLocalLockedMode()
    {
        var document = LoadXml("Directory.Build.props");

        var lockFileProperties = document.Descendants("RestorePackagesWithLockFile").ToArray();

        lockFileProperties.Should().ContainSingle();
        lockFileProperties[0]
            .Value
            .Should()
            .Be("true");

        var lockedModeProperties = document.Descendants("RestoreLockedMode").ToArray();

        lockedModeProperties.Should().ContainSingle();
        var lockedMode = lockedModeProperties[0];

        lockedMode.Value.Should().Be("true");
        lockedMode.Attribute("Condition")?.Value.Should().Contain("GITHUB_ACTIONS");
        lockedMode.Attribute("Condition")?.Value.Should().Contain("TF_BUILD");
    }

    [Fact]
    public void Projects_ShouldCommitNuGetLockFiles()
    {
        foreach (var projectPath in ProjectFiles)
        {
            var lockFilePath = Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.lock.json");

            File.Exists(GetRepoFilePath(lockFilePath))
                .Should()
                .BeTrue($"{projectPath} should have a committed packages.lock.json");
        }
    }

    [Fact]
    public void GitHubWorkflows_ShouldUseLockedModeForProjectRestore()
    {
        var restoreLines = Directory.GetFiles(GetRepoFilePath(".github/workflows"), "*.yml")
            .SelectMany(File.ReadAllLines)
            .Select(line => line.Trim())
            .Where(line => line.Contains("dotnet restore", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("dotnet tool restore", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        restoreLines.Should().NotBeEmpty();
        restoreLines.Should().OnlyContain(
            line => line.Contains("--locked-mode", StringComparison.OrdinalIgnoreCase),
            "CI project restore should fail when packages.lock.json is stale");
    }

    private static XDocument LoadXml(string relativePath)
    {
        var path = GetRepoFilePath(relativePath);

        File.Exists(path).Should().BeTrue($"{relativePath} should exist");

        return XDocument.Load(path);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
