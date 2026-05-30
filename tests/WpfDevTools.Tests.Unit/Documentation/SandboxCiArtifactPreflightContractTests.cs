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
        var runtimeSmoke = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "tools", "packaging", "Test-PackagedServerRuntime.ps1"));

        launcher.Should().Contain("PackageArchivePath");
        launcher.Should().Contain("SandboxCi.ArtifactPreflight.ps1");
        launcher.Should().Contain("Test-PackagedServerRuntime.ps1");
        launcher.Should().Contain("Test-InstallResidue.ps1");
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
        runner.Should().Contain("Test-InstallResidue.ps1");
        runner.Should().Contain(@"..\tools\packaging\Test-PackagedServerRuntime.ps1");
        runner.Should().Contain("dotnet-install.ps1");
        runner.Should().Contain("DOTNET_ROOT");
        runner.Should().Contain("DOTNET_ROOT(x86)");
        runner.Should().Contain("Get-DotNetRuntimeArchitecture");
        runner.Should().Contain("-Architecture");
        runner.Should().Contain("$dotNetRuntimeArchitecture");
        runner.Should().Contain("--list-runtimes");
        runner.Should().Contain("Invoke-ExternalWithTimeout");
        runner.Should().Contain(@"logs\process\$timestamp-$safeName");
        runner.Should().Contain("Process logs:");
        runner.Should().Contain("-TimeoutSeconds 900");
        runner.Should().Contain("preflight-summary.json");
        runner.Should().Contain("WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA");
        runner.Should().Contain("$script:WpfDevToolsInstallerTestModeHarnessEnabled = $true");
        runner.Should().Contain("Set-StrictMode -Off");
        runner.Should().Contain("Set-StrictMode -Version Latest");
        runner.Should().Contain(". $ScriptPath @Parameters");
        runner.Should().Contain("Invoke-InstallerStep -Name 'Install package-local release' -ScriptPath $installScript -Parameters @{");
        runner.Should().Contain("Invoke-InstallerStep -Name 'Uninstall package-local release' -ScriptPath $installedScript -Parameters @{");
        runner.Should().Contain("Invoke-InstallerStep -Name 'Reinstall package-local release' -ScriptPath $installScript -Parameters @{");
        runner.Should().Contain("Invoke-InstallerStep -Name 'Full uninstall package-local release' -ScriptPath $installedScript -Parameters @{");
        runner.Should().Contain("Invoke-RuntimeSmoke -Name 'Run packaged server runtime smoke first run'");
        runner.Should().Contain("Invoke-RuntimeSmoke -Name 'Run packaged server runtime smoke second run'");
        runner.Should().Contain("Invoke-RuntimeSmoke -Name 'Run packaged server runtime smoke after transport state corruption'");
        runner.Should().Contain("Invoke-DefaultTransportStateCorruptionProbe");
        runner.Should().Contain("shared-secret.bin");
        runner.Should().Contain(".corrupt-");
        runner.Should().Contain("Run install residue validation");
        runner.Should().Contain("Assert-NoUnexpectedIgnoredArtifacts");
        runner.Should().Contain("-TargetProcessPath");
        runner.Should().Contain("PASS $RunId");
        runner.Should().Contain("FAIL $RunId");

        runtimeSmoke.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
        runtimeSmoke.Should().Contain("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS");
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
            var preflightRoot = Path.Combine(workRoot, "preflight");
            var document = XDocument.Load(configPath);
            var sandboxFolders = document.Descendants("SandboxFolder").Select(element => element.Value).ToArray();
            var command = document.Descendants("Command").Single().Value;

            File.Exists(Path.Combine(preflightRoot, "SandboxCi.ArtifactPreflight.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(preflightRoot, "SandboxCi.ProcessCleanup.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(preflightRoot, "Test-PackagedServerRuntime.ps1")).Should().BeTrue();
            sandboxFolders.Should().Contain(new[] { @"C:\release", @"C:\preflight", @"C:\preflight-output" });
            document.Descendants("MappedFolder").Should().HaveCount(3);
            document.Descendants("ReadOnly").Select(element => element.Value)
                .Should().ContainInOrder("true", "true", "false");
            command.Should().StartWith("powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ");
            command.Should().NotContain("'");
            var decodedCommand = DecodeSandboxEncodedCommand(command);
            decodedCommand.Should().Contain("SandboxCi.ArtifactPreflight.ps1");
            decodedCommand.Should().Contain(@"-PackageArchivePath 'C:\release\release_1.0.0_win-x64.zip'");
            decodedCommand.Should().Contain(@"-OutputRoot 'C:\preflight-output'");
            command.Should().NotContain("Start-SandboxCi.ps1");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void InvokeWindowsSandboxArtifactPreflight_GenerateOnly_ShouldSingleQuotePowerShellExpandableArguments()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workRoot = Path.Combine(tempRoot, "work root");
            var packageRoot = Path.Combine(tempRoot, "release output");
            Directory.CreateDirectory(packageRoot);
            var packageArchive = Path.Combine(packageRoot, "release_1.0.0_win-x64_$(Write-Output injected).zip");
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
            var command = XDocument.Load(configPath).Descendants("Command").Single().Value;
            var decodedCommand = DecodeSandboxEncodedCommand(command);

            decodedCommand.Should().Contain(@"-PackageArchivePath 'C:\release\release_1.0.0_win-x64_$(Write-Output injected).zip'");
            command.Should().NotContain("$(",
                "the native Windows Sandbox logon command should carry the PowerShell script through EncodedCommand");
            command.Should().NotContain(@"""C:\release\release_1.0.0_win-x64_$(Write-Output injected).zip""",
                "double-quoted PowerShell arguments would execute $() subexpressions in the Sandbox logon command");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void InvokeWindowsSandboxArtifactPreflight_GenerateOnly_ShouldQuoteLeadingDashValues()
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
                "-DotNetChannel:-SkipDotNetProvisioning",
                "-GenerateOnly");

            result.ExitCode.Should().Be(0, result.Output);
            var configPath = Directory.GetFiles(workRoot, "WpfDevTools-ArtifactPreflight-*.wsb").Should().ContainSingle().Subject;
            var command = XDocument.Load(configPath).Descendants("Command").Single().Value;
            var decodedCommand = DecodeSandboxEncodedCommand(command);

            decodedCommand.Should().Contain("-DotNetChannel '-SkipDotNetProvisioning'");
            decodedCommand.Should().NotContain("-DotNetChannel -SkipDotNetProvisioning",
                "leading-dash values must not be treated as switches by the generated PowerShell command");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void InvokeWindowsSandboxArtifactPreflight_ShouldRejectUnsupportedArgumentsBeforeGeneratingConfig()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var packageArchive = Path.Combine(tempRoot, "release_1.0.0_win-x64.zip");
            File.WriteAllBytes(packageArchive, []);
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "Invoke-WindowsSandboxArtifactPreflight.ps1");

            RunPowerShellFile(
                    scriptPath,
                    "-PackageArchivePath",
                    packageArchive,
                    "-WorkRoot",
                    Path.Combine(tempRoot, "client"),
                    "-Client",
                    "other;Write-Host unsafe",
                    "-GenerateOnly")
                .ExitCode.Should().NotBe(0);

            RunPowerShellFile(
                    scriptPath,
                    "-PackageArchivePath",
                    packageArchive,
                    "-WorkRoot",
                    Path.Combine(tempRoot, "url"),
                    "-DotNetInstallScriptUrl",
                    "http://example.invalid/dotnet-install.ps1",
                    "-GenerateOnly")
                .ExitCode.Should().NotBe(0);

            RunPowerShellFile(
                    scriptPath,
                    "-PackageArchivePath",
                    packageArchive,
                    "-WorkRoot",
                    Path.Combine(tempRoot, "arm64"),
                    "-Architecture",
                    "arm64",
                    "-GenerateOnly")
                .ExitCode.Should().NotBe(0);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxArtifactPreflight_ShouldIsolateDefaultTransportStateBeforeCorruptionProbe()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.ArtifactPreflight.ps1");

        runner.Should().Contain("$preflightProfileRoot = Join-Path $localRoot 'profile'");
        runner.Should().Contain("$env:APPDATA = Join-Path $preflightProfileRoot 'AppData\\Roaming'");
        runner.Should().Contain("$env:LOCALAPPDATA = Join-Path $preflightProfileRoot 'AppData\\Local'");
        runner.Should().Contain("$env:USERPROFILE = $preflightProfileRoot");
        runner.IndexOf("$env:APPDATA = Join-Path $preflightProfileRoot", StringComparison.Ordinal)
            .Should().BeLessThan(runner.LastIndexOf("Invoke-DefaultTransportStateCorruptionProbe", StringComparison.Ordinal),
                "the corruption probe must only mutate the isolated preflight profile, never the caller's real WpfDevTools state");
    }

    [Fact]
    public void ContributorTestingGuide_ShouldDocumentArtifactPreflightWorkflow()
    {
        var english = File.ReadAllText(Path.Combine(RepoRoot, "docfx", "contributors", "testing-and-tdd.md"));
        var zhTw = File.ReadAllText(Path.Combine(RepoRoot, "docfx", "zh-tw", "contributors", "testing-and-tdd.md"));

        english.Should().Contain("Invoke-WindowsSandboxArtifactPreflight.ps1");
        english.Should().Contain("Windows Sandbox local preflight");
        english.Should().Contain("not a GitHub Actions parity guarantee");
        english.Should().Contain("covers coverage, x64/x86/arm64 release packaging smoke, NuGet pack");
        english.Should().Contain("exact x64 and x86 native bootstrapper builds");
        english.Should().Contain("Debug/Release x64 and x86 solution builds");
        english.Should().Contain("ARM64 Release cross-build");
        english.Should().Contain("local DocFX Pages build steps");
        english.Should().Contain("does not cover x86 test execution or self-hosted ARM64 runtime smoke lanes");
        english.Should().Contain("GitHub Pages upload/deployment");
        english.Should().Contain("GitHub artifact upload/download boundaries");
        english.Should().Contain("GitHub-hosted runner image differences");
        english.Should().Contain("unsigned local package smoke");
        english.Should().Contain("first run");
        english.Should().Contain("second run");
        english.Should().Contain("corrupt transport state");
        english.Should().Contain("full-uninstall");
        english.Should().Contain("residue validation");
        english.Should().Contain("registration metadata checks remain covered by the installer/client registration tests");
        english.Should().Contain("Publish-Release.ps1");
        english.Should().Contain("Get-ChildItem");
        english.Should().Contain("-DotNetChannel");
        english.Should().Contain("setup-dotnet");
        english.Should().Contain("-WhatIf");
        english.Should().Contain("-Force");
        english.Should().Contain("-Confirm:$false");
        english.Should().NotContain("Windows Sandbox CI simulation");
        english.Should().NotContain("same CI-oriented PowerShell entrypoints that GitLab/GitHub jobs use");
        english.Should().NotContain("release_0.1.0_win-x64.zip");

        zhTw.Should().Contain("Invoke-WindowsSandboxArtifactPreflight.ps1");
        zhTw.Should().Contain("Windows Sandbox 本機 preflight");
        zhTw.Should().Contain("不等同 GitHub Actions parity 保證");
        zhTw.Should().Contain("涵蓋 coverage、x64/x86/arm64 release packaging smoke、NuGet pack");
        zhTw.Should().Contain("x64 與 x86 native bootstrapper builds");
        zhTw.Should().Contain("Debug/Release x64 與 x86 solution builds");
        zhTw.Should().Contain("ARM64 Release cross-build");
        zhTw.Should().Contain("本機 DocFX Pages build steps");
        zhTw.Should().Contain("不涵蓋 x86 test execution 或 self-hosted ARM64 runtime smoke lanes");
        zhTw.Should().Contain("GitHub Pages upload/deployment");
        zhTw.Should().Contain("GitHub artifact upload/download 邊界");
        zhTw.Should().Contain("GitHub-hosted runner image 差異");
        zhTw.Should().Contain("unsigned local package smoke");
        zhTw.Should().Contain("first run");
        zhTw.Should().Contain("second run");
        zhTw.Should().Contain("corrupt transport state");
        zhTw.Should().Contain("full-uninstall");
        zhTw.Should().Contain("residue validation");
        zhTw.Should().Contain("registration metadata 仍由 installer/client registration 測試覆蓋");
        zhTw.Should().Contain("Publish-Release.ps1");
        zhTw.Should().Contain("Get-ChildItem");
        zhTw.Should().Contain(".NET runtime");
        zhTw.Should().Contain("-DotNetChannel");
        zhTw.Should().Contain("setup-dotnet");
        zhTw.Should().Contain("-WhatIf");
        zhTw.Should().Contain("-Force");
        zhTw.Should().Contain("-Confirm:$false");
        zhTw.Should().NotContain("Windows Sandbox 模擬 CI");
        zhTw.Should().NotContain("與 GitLab/GitHub jobs 對齊的 CI PowerShell 入口");
        zhTw.Should().NotContain("release_0.1.0_win-x64.zip");
    }

    private static string DecodeSandboxEncodedCommand(string command)
    {
        const string prefix = "powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ";
        command.Should().StartWith(prefix);
        var encodedCommand = command[prefix.Length..].Trim();
        return System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encodedCommand));
    }
}
