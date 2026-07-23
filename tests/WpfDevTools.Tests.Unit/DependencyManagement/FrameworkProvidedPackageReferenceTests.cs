using System.Xml.Linq;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.DependencyManagement;

public sealed class FrameworkProvidedPackageReferenceTests
{
    private static readonly string[] FrameworkProvidedPackages =
    [
        "System.Net.Http",
        "System.Text.RegularExpressions"
    ];

    [Theory]
    [MemberData(nameof(FrameworkProvidedPackageNames))]
    public void DirectoryPackages_ShouldNotPinNet8FrameworkProvidedBclPackages(string packageName)
    {
        var packageVersions = LoadProject("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>();

        packageVersions.Should().NotContain(packageName,
            $"{packageName} is provided by net8.0 and must not be centrally pinned to an obsolete package version");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj")]
    [InlineData("tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj")]
    public void Net8TestProjects_ShouldNotDirectlyReferenceFrameworkProvidedBclPackages(string projectPath)
    {
        var packageReferences = LoadProject(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>();

        packageReferences.Should().NotContain(FrameworkProvidedPackages,
            "net8.0-windows test projects should use the framework-provided assemblies instead of obsolete compatibility packages");
    }

    public static IEnumerable<object[]> FrameworkProvidedPackageNames()
        => FrameworkProvidedPackages.Select(packageName => new object[] { packageName });

    [Fact]
    public void McpServerSystemTextJsonPin_ShouldUseCurrentStablePatch()
    {
        var packageVersion = LoadProject("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Single(element =>
                element.Attribute("Include")?.Value == "System.Text.Json" &&
                element.Attribute("Condition")?.Value == "'$(MSBuildProjectName)' == 'WpfDevTools.Mcp.Server'");

        var version = packageVersion.Attribute("Version")?.Value;

        version.Should().Be("10.0.9",
            "the MCP SDK requires the 10.x System.Text.Json line, so the server should pin the current stable servicing patch rather than an older build");
        version.Should().NotContain("-");
    }

    [Fact]
    public void McpServerHostingPin_ShouldAlignWithMcpSdkExtensionStack()
    {
        var packageVersion = LoadProject("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Single(element => element.Attribute("Include")?.Value == "Microsoft.Extensions.Hosting");

        var version = packageVersion.Attribute("Version")?.Value;

        version.Should().Be("10.0.9",
            "ModelContextProtocol 1.4.0 depends on the Microsoft.Extensions 10.x stack, so the server should not mix an 8.x Hosting package with 10.x hosting abstractions");
        version.Should().NotContain("-");
    }

    [Fact]
    public void McpSdkPackagePin_ShouldUseCurrentStableRelease()
    {
        var packageVersion = LoadProject("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Single(element => element.Attribute("Include")?.Value == "ModelContextProtocol");

        var version = packageVersion.Attribute("Version")?.Value;

        version.Should().Be("1.4.0",
            "NuGet publishes ModelContextProtocol 1.4.0 as the current stable MCP C# SDK package");
        version.Should().NotContain("-");
    }

    [Theory]
    [InlineData("System.IO.FileSystem.AccessControl")]
    [InlineData("System.IO.Pipes.AccessControl")]
    public void SecurityRelevantAccessControlPackages_ShouldBeNet48CompatibilityOnly(string packageName)
    {
        var packageVersion = LoadProject("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Single(element => element.Attribute("Include")?.Value == packageName);

        packageVersion.Attribute("Condition")?.Value.Should().Be("'$(TargetFramework)' == 'net48'",
            $"{packageName} has no net8-era stable NuGet package and should only be pinned for .NET Framework compatibility");
    }

    [Fact]
    public void Net48Projects_ShouldReferencePortableFrameworkAssemblies()
    {
        const string packageName = "Microsoft.NETFramework.ReferenceAssemblies";
        var packageVersion = LoadProject("Directory.Packages.props")
            .Descendants("PackageVersion")
            .Single(element => element.Attribute("Include")?.Value == packageName);

        packageVersion.Attribute("Version")?.Value.Should().Be("1.0.3");
        packageVersion.Attribute("Condition")?.Value.Should().Be("'$(TargetFramework)' == 'net48'");

        var repoRoot = Path.GetDirectoryName(TestRepositoryPaths.GetRepoFilePath("WpfDevTools.sln"))!;
        var net48Projects = new[] { "src", "tests" }
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(repoRoot, directory),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(path => LoadProjectFile(path)
                .Descendants()
                .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .Any(element => element.Value.Split(';').Contains("net48", StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        net48Projects.Should().NotBeEmpty();
        foreach (var projectPath in net48Projects)
        {
            var reference = LoadProjectFile(projectPath)
                .Descendants("PackageReference")
                .Single(element => element.Attribute("Include")?.Value == packageName);

            reference.Attribute("PrivateAssets")?.Value.Should().BeEquivalentTo("all");
            reference.Parent?.Attribute("Condition")?.Value.Should().Be("'$(TargetFramework)' == 'net48'");
        }
    }

    private static XDocument LoadProject(string relativePath)
        => XDocument.Load(TestRepositoryPaths.GetRepoFilePath(relativePath));

    private static XDocument LoadProjectFile(string path)
        => XDocument.Load(path);
}
