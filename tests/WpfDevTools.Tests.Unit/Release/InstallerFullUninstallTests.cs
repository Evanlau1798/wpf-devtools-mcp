using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerFullUninstallTests
{
    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRemoveAllDetectedRegistrationsAndInstallerOwnedServerFiles()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");

            RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "vscode", "-VsCodeConfigPath", vscodeConfigPath, "-NonInteractive", "-Force", "-OutputJson"])
                .ExitCode.Should().Be(0);
            RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "visual-studio", "-VisualStudioConfigPath", visualStudioConfigPath, "-NonInteractive", "-OutputJson"])
                .ExitCode.Should().Be(0);

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-InstallRoot", installRoot, "-VsCodeConfigPath", vscodeConfigPath, "-VisualStudioConfigPath", visualStudioConfigPath, "-NonInteractive", "-Force", "-OutputJson"]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(vscodeConfigPath).Should().NotContain("wpf-devtools");
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");

            using var json = JsonDocument.Parse(uninstall.Stdout);
            var statePath = json.RootElement.GetProperty("statePath").GetString();
            using var state = JsonDocument.Parse(File.ReadAllText(statePath!));
            state.RootElement.GetProperty("registrations").EnumerateObject().Should().BeEmpty();
            state.RootElement.GetProperty("architectures").EnumerateObject().Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldNotDeleteExternalExecutables()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var externalRoot = Path.Combine(tempRoot, "external");
            var externalExecutable = Path.Combine(externalRoot, "wpf-devtools-x64.exe");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            Directory.CreateDirectory(externalRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(vscodeConfigPath)!);
            File.WriteAllText(externalExecutable, "stub");
            File.WriteAllText(
                vscodeConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"" + externalExecutable.Replace("\\", "\\\\") + "\",\"args\":[]}}}");

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-VsCodeConfigPath", vscodeConfigPath, "-NonInteractive", "-Force", "-OutputJson"]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            File.Exists(externalExecutable).Should().BeTrue();
            File.ReadAllText(vscodeConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldTreatCaseVariantConfigPathsAsInstallerOwned()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");

            var install = RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "vscode", "-VsCodeConfigPath", vscodeConfigPath, "-NonInteractive", "-Force", "-OutputJson"]);

            install.ExitCode.Should().Be(0, install.Stderr);

            using var installJson = JsonDocument.Parse(install.Stdout);
            var statePath = installJson.RootElement.GetProperty("statePath").GetString();
            statePath.Should().NotBeNullOrWhiteSpace();
            File.Delete(statePath!);

            var installedExecutable = installJson.RootElement.GetProperty("installedExecutable").GetString();
            installedExecutable.Should().NotBeNullOrWhiteSpace();
            var caseVariantExecutable = installedExecutable!.ToUpperInvariant();
            File.WriteAllText(
                vscodeConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"" + caseVariantExecutable.Replace("\\", "\\\\") + "\",\"args\":[]}}}");

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-InstallRoot", installRoot, "-VsCodeConfigPath", vscodeConfigPath, "-NonInteractive", "-Force", "-OutputJson"]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(vscodeConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldTreatSlashNormalizedConfigPathsAsInstallerOwned()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");

            var install = RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "vscode", "-VsCodeConfigPath", vscodeConfigPath, "-NonInteractive", "-Force", "-OutputJson"]);

            install.ExitCode.Should().Be(0, install.Stderr);

            using var installJson = JsonDocument.Parse(install.Stdout);
            var statePath = installJson.RootElement.GetProperty("statePath").GetString();
            statePath.Should().NotBeNullOrWhiteSpace();
            File.Delete(statePath!);

            var installedExecutable = installJson.RootElement.GetProperty("installedExecutable").GetString();
            installedExecutable.Should().NotBeNullOrWhiteSpace();
            var slashNormalizedExecutable = installedExecutable!
                .Replace("\\", "/")
                .Replace("/current/", "/CURRENT/")
                .Replace("/bin/", "/BIN/");

            File.WriteAllText(
                vscodeConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"" + slashNormalizedExecutable.Replace("\\", "\\\\") + "\",\"args\":[]}}}");

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-InstallRoot", installRoot, "-VsCodeConfigPath", vscodeConfigPath, "-NonInteractive", "-Force", "-OutputJson"]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(vscodeConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRecoverCustomVisualStudioRegistrationWhenStateFileIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customVisualStudioConfigPath = Path.Combine(tempRoot, "custom", "visual-studio", ".mcp.json");
            var environment = CreateInstallerEnvironment(tempRoot);

            var install = RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "visual-studio", "-VisualStudioConfigPath", customVisualStudioConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            File.Delete(statePath);

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-InstallRoot", installRoot, "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(customVisualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRecoverCustomCursorRegistrationWhenStateFileIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customCursorConfigPath = Path.Combine(tempRoot, "custom", "cursor", "mcp.json");
            var environment = CreateInstallerEnvironment(tempRoot);

            var install = RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", customCursorConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customCursorConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            File.Delete(statePath);

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-InstallRoot", installRoot, "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(customCursorConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRecoverCustomCursorProjectRegistrationWhenStateFileIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var projectRoot = Path.Combine(tempRoot, "CustomCursorProject");
            var projectConfigPath = Path.Combine(projectRoot, ".cursor", "mcp.json");
            var environment = CreateInstallerEnvironment(tempRoot);
            environment["APPDATA"] = appData;
            environment["LOCALAPPDATA"] = localAppData;
            environment["USERPROFILE"] = userProfile;

            var install = RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(projectConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            File.Delete(statePath);

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-InstallRoot", installRoot, "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(projectConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRemoveCursorGlobalAndProjectRegistrationsTogether()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var globalConfigPath = Path.Combine(tempRoot, "UserProfile", ".cursor", "mcp.json");
            var projectRoot = Path.Combine(tempRoot, "CursorProject");
            var projectConfigPath = Path.Combine(projectRoot, ".cursor", "mcp.json");
            var environment = CreateInstallerEnvironment(tempRoot);

            RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                environment)
                .ExitCode.Should().Be(0);

            RunInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson"],
                environment)
                .ExitCode.Should().Be(0);

            File.ReadAllText(globalConfigPath).Should().Contain("wpf-devtools");
            File.ReadAllText(projectConfigPath).Should().Contain("wpf-devtools");

            var uninstall = RunInstaller(
                tempRoot,
                ["-Action", "full-uninstall", "-Architecture", "x64", "-InstallRoot", installRoot, "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(globalConfigPath).Should().NotContain("wpf-devtools");
            File.ReadAllText(projectConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRollbackEarlierRegistrationChangesWhenLaterRemovalFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var installBase = Path.Combine(tempRoot, "install-root", "x64");
            var installedExecutable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(vscodeConfigPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(visualStudioConfigPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(installedExecutable)!);
            File.WriteAllText(vscodeConfigPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"C:\\\\tool.exe\",\"args\":[]}}}");
            File.WriteAllText(visualStudioConfigPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"C:\\\\tool.exe\",\"args\":[]}}}");
            File.WriteAllText(installedExecutable, "stub");

            var command = string.Join(" ; ",
            [
                ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Uninstall.ps1").Replace("'", "''") + "'",
                "function Get-DetectedInstallerRegistrations { param($State) return @(" +
                    "[ordered]@{ ClientId='vscode'; RegistrationMode='json-file'; RegistrationTarget='" + vscodeConfigPath.Replace("'", "''") + "'; InstallRoot='" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "'; Architecture='x64'; InstalledExecutable='" + installedExecutable.Replace("'", "''") + "'; InstallerOwned=$true }," +
                    "[ordered]@{ ClientId='visual-studio'; RegistrationMode='json-file'; RegistrationTarget='" + visualStudioConfigPath.Replace("'", "''") + "'; InstallRoot='" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "'; Architecture='x64'; InstalledExecutable='" + installedExecutable.Replace("'", "''") + "'; InstallerOwned=$true }) }",
                "function Get-DetectedInstallerInstallations { param($State) return @([ordered]@{ InstallRoot='" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "'; Architecture='x64'; InstallBase='" + installBase.Replace("'", "''") + "'; InstalledExecutable='" + installedExecutable.Replace("'", "''") + "'; InstallerOwned=$true }) }",
                "function Invoke-ClientUnregistration { param([string]$SelectedClient, $RegistrationRecord) if ($SelectedClient -eq 'vscode') { '{}' | Set-Content -Path '" + vscodeConfigPath.Replace("'", "''") + "' -Encoding UTF8; return @([ordered]@{ client='vscode'; mode='json-file'; target='" + vscodeConfigPath.Replace("'", "''") + "'; backupPath=$null; applied=$true }) }; throw 'simulated failure' }",
                "function Invoke-UninstallVerification { param([string]$SelectedClient, $RegistrationRecord) return @{ Succeeded = $true; VerificationMessage = 'ok' } }",
                "function Resolve-InstallBasePath { param([string]$ResolvedInstallRoot, [string]$ResolvedArchitecture) return '" + installBase.Replace("'", "''") + "' }",
                "function Remove-PathIfExists { param([string]$Path) if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force } }",
                "function Get-EmptyInstallerState { return [ordered]@{ lastInstallRoot=$null; architectures=[ordered]@{}; registrations=[ordered]@{} } }",
                "function Save-InstallerState { param($State) return 'state.json' }",
                "function Undo-ClientRegistrationChanges { param([string]$SelectedClient, [object[]]$Registrations, [string]$RollbackMode, [string]$InstalledExecutable, [string]$InstallBase, $RegistrationRecord) }",
                "try { Invoke-InstallerFullUninstallCore -State ([ordered]@{}) | Out-Null } catch { }",
                "@{ VscodeConfig = ([System.IO.File]::ReadAllText('" + vscodeConfigPath.Replace("'", "''") + "')); InstallExists = (Test-Path -LiteralPath '" + installBase.Replace("'", "''") + "') } | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("InstallExists").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("VscodeConfig").GetString().Should().Contain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeInstallerFullUninstallCore_ShouldRollbackRegistrationsAndInstallationsWhenStateSaveFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var installedExecutable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(vscodeConfigPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(installedExecutable)!);
            File.WriteAllText(
                vscodeConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"" + installedExecutable.Replace("\\", "\\\\") + "\",\"args\":[]}}}");
            File.WriteAllText(installedExecutable, "installed");

            var command = string.Join(" ; ",
            [
                ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Uninstall.ps1").Replace("'", "''") + "'",
                "function Resolve-ClientStateKey { param([string]$ClientId, [string]$RegistrationMode) return $ClientId }",
                "function Resolve-ClientBaseId { param([string]$ClientId) return $ClientId }",
                "function Get-DetectedInstallerRegistrations { param($State) return @([ordered]@{ ClientId='vscode'; RegistrationMode='json-file'; RegistrationTarget='" + vscodeConfigPath.Replace("'", "''") + "'; InstallRoot='" + installRoot.Replace("'", "''") + "'; Architecture='x64'; InstalledExecutable='" + installedExecutable.Replace("'", "''") + "'; InstallerOwned=$true }) }",
                "function Get-DetectedInstallerInstallations { param($State) return @([ordered]@{ InstallRoot='" + installRoot.Replace("'", "''") + "'; Architecture='x64'; InstallBase='" + installBase.Replace("'", "''") + "'; InstalledExecutable='" + installedExecutable.Replace("'", "''") + "'; InstallerOwned=$true }) }",
                "function Invoke-UninstallVerification { param([string]$SelectedClient, $RegistrationRecord) return @{ Succeeded = $true; VerificationMessage = 'ok' } }",
                "function Resolve-InstallBasePath { param([string]$ResolvedInstallRoot, [string]$ResolvedArchitecture) return '" + installBase.Replace("'", "''") + "' }",
                "function Remove-PathIfExists { param([string]$Path) if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force } }",
                "function Get-EmptyInstallerState { return [ordered]@{ lastInstallRoot=$null; architectures=[ordered]@{}; registrations=[ordered]@{} } }",
                "function Save-InstallerState { param($State) throw 'simulated state save failure' }",
                "try { Invoke-InstallerFullUninstallCore -State ([ordered]@{ lastInstallRoot='" + installRoot.Replace("'", "''") + "'; architectures=[ordered]@{}; registrations=[ordered]@{} }) | Out-Null } catch { }",
                "@{ Config = ([System.IO.File]::ReadAllText('" + vscodeConfigPath.Replace("'", "''") + "')); InstallExists = (Test-Path -LiteralPath '" + installBase.Replace("'", "''") + "'); ExecutableExists = (Test-Path -LiteralPath '" + installedExecutable.Replace("'", "''") + "') } | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("Config").GetString().Should().Contain("wpf-devtools");
            json.RootElement.GetProperty("InstallExists").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("ExecutableExists").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunInstaller(
        string tempRoot,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
        => ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            arguments,
            CreateInstallerEnvironment(tempRoot, environmentOverrides));

    private static Dictionary<string, string?> CreateInstallerEnvironment(
        string tempRoot,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        return environment;
    }
}
