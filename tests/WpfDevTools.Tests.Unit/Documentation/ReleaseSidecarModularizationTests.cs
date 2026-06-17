using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ReleaseSidecarModularizationTests
{
    [Fact]
    public void WriteReleaseSidecars_ShouldLoadSbomSubsystemFromPackagingModule()
    {
        var root = FindRepoRoot();
        var sidecarPath = Path.Combine(root, "scripts", "tools", "packaging", "Write-ReleaseSidecars.ps1");
        var modulePath = Path.Combine(root, "scripts", "tools", "packaging", "Write-ReleaseSbomDocuments.ps1");
        var exceptionPath = Path.Combine(
            root,
            "tests",
            "WpfDevTools.Tests.Unit",
            "Documentation",
            "LineLimitExceptions.txt");

        File.Exists(modulePath).Should().BeTrue(
            "release SBOM document generation should live outside the sidecar orchestration entrypoint");

        var sidecarScript = File.ReadAllText(sidecarPath);
        sidecarScript.Should().Contain("Write-ReleaseSbomDocuments.ps1");
        sidecarScript.Should().NotContain("function New-PackageSbom");
        sidecarScript.Should().NotContain("function New-ReleaseSbom");

        File.ReadLines(sidecarPath).Count().Should().BeLessThanOrEqualTo(500);
        File.ReadLines(modulePath).Count().Should().BeLessThanOrEqualTo(500);

        File.ReadAllText(exceptionPath).Should().NotContain("scripts/tools/packaging/Write-ReleaseSidecars.ps1");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
