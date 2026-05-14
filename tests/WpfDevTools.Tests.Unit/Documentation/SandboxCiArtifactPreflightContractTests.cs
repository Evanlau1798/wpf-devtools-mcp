using FluentAssertions;
using System.Xml.Linq;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxArtifactPreflightScripts_ShouldExposeArtifactOnlyWorkflow()
    {
        var scriptRoot = Path.Combine(RepoRoot, "scripts", "ci");
        var launcher = ReadScript(scriptRoot, "Invoke-WindowsSandboxArtifactPreflight.ps1");
        var runner = ReadScript(scriptRoot, "SandboxCi.ArtifactPreflight.ps1");

        launcher.Should().Contain("PackageArchivePath");
        launcher.Should().Contain("SandboxCi.ArtifactPreflight.ps1");
        launcher.Should().Contain("Test-PackagedServerRuntime.ps1");
        launcher.Should().Contain(@"C:\release");
        launcher.Should().Contain(@"C:\preflight");
        launcher.Should().Contain(@"C:\preflight-output");
        launcher.Should().Contain("last-result.txt");
        launcher.Should().Contain("Set-SandboxHostScheduling");
        launcher.Should().NotContain("Start-SandboxCi.ps1",
            "artifact preflight should consume a release package instead of rebuilding the source tree inside Sandbox");
        launcher.Should().NotContain("Resolve-VisualStudioInstallRoot",
            "artifact preflight should not depend on a mapped developer toolchain");

        runner.Should().Contain("Expand-Archive");
        runner.Should().Contain(@"bin\install.ps1");
        runner.Should().Contain(@"bin\manifest.json");
        runner.Should().Contain("Test-PackagedServerRuntime.ps1");
        runner.Should().Contain(@"..\tools\packaging\Test-PackagedServerRuntime.ps1");
        runner.Should().Contain("dotnet-install.ps1");
        runner.Should().Contain("DOTNET_ROOT");
        runner.Should().Contain("--list-runtimes");
        runner.Should().Contain(".stdout.log");
        runner.Should().Contain(".stderr.log");
        runner.Should().Contain("$ErrorActionPreference = 'Continue'");
        runner.Should().Contain("preflight-summary.json");
        runner.Should().Contain("WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA");
        runner.Should().Contain("$script:WpfDevToolsInstallerTestModeHarnessEnabled = $true");
        runner.Should().Contain("Set-StrictMode -Off");
        runner.Should().Contain("Set-StrictMode -Version Latest");
        runner.Should().Contain(". $ScriptPath @Parameters");
        runner.Should().Contain("Invoke-InstallerStep -Name 'Install package-local release' -ScriptPath $installScript -Parameters @{");
        runner.Should().Contain("Invoke-InstallerStep -Name 'Uninstall package-local release' -ScriptPath $installedScript -Parameters @{");
        runner.Should().Contain("PASS $RunId");
        runner.Should().Contain("FAIL $RunId");
    }

    [Fact]
    public void InvokeWindowsSandboxArtifactPreflight_GenerateOnly_ShouldWriteArtifactOnlySandboxConfig()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workRoot = Path.Combine(tempRoot, "work root");
            var packageRoot = Path.Combine(tempRoot, "release output");
            Directory.CreateDirectory(packageRoot);
            var packageArchive = Path.Combine(packageRoot, "release_1.0.0_win-x64.zip");
            File.WriteAllBytes(packageArchive, []);

            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "Invoke-WindowsSandboxArtifactPreflight.ps1");
            var result = RunPowerShellFile(
                scriptPath,
                "-PackageArchivePath",
                packageArchive,
                "-WorkRoot",
                workRoot,
                "-GenerateOnly");

            result.ExitCode.Should().Be(0, result.Output);
            var configPath = Directory.GetFiles(workRoot, "WpfDevTools-ArtifactPreflight-*.wsb").Should().ContainSingle().Subject;
            var document = XDocument.Load(configPath);
            var sandboxFolders = document.Descendants("SandboxFolder").Select(element => element.Value).ToArray();
            var command = document.Descendants("Command").Single().Value;

            sandboxFolders.Should().Contain(new[] { @"C:\release", @"C:\preflight", @"C:\preflight-output" });
            document.Descendants("MappedFolder").Should().HaveCount(3);
            document.Descendants("ReadOnly").Select(element => element.Value)
                .Should().ContainInOrder("true", "true", "false");
            command.Should().Contain("SandboxCi.ArtifactPreflight.ps1");
            command.Should().Contain(@"-PackageArchivePath ""C:\release\release_1.0.0_win-x64.zip""");
            command.Should().Contain(@"-OutputRoot ""C:\preflight-output""");
            command.Should().NotContain("Start-SandboxCi.ps1");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void ContributorTestingGuide_ShouldDocumentArtifactPreflightWorkflow()
    {
        var english = File.ReadAllText(Path.Combine(RepoRoot, "docfx", "contributors", "testing-and-tdd.md"));
        var zhTw = File.ReadAllText(Path.Combine(RepoRoot, "docfx", "zh-tw", "contributors", "testing-and-tdd.md"));

        english.Should().Contain("Invoke-WindowsSandboxArtifactPreflight.ps1");
        english.Should().Contain("Publish-Release.ps1");
        english.Should().Contain("-DotNetChannel");
        english.Should().Contain("setup-dotnet");

        zhTw.Should().Contain("Invoke-WindowsSandboxArtifactPreflight.ps1");
        zhTw.Should().Contain("Publish-Release.ps1");
        zhTw.Should().Contain(".NET runtime");
        zhTw.Should().Contain("-DotNetChannel");
        zhTw.Should().Contain("setup-dotnet");
    }

}
