using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerPathSafetyTests
{
    [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);

    [Fact]
    public void ResolveAbsoluteDirectory_ShouldRejectUncPathsBeforeCreatingDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var command = string.Join(" ; ",
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action install -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -NonInteractive"),
                "Resolve-AbsoluteDirectory -Path '\\\\server\\share\\WpfDevToolsMcp'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("local path");
            Directory.Exists(Path.Combine(tempRoot, "server")).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RemovePathIfExists_ShouldRejectReparsePointTargets()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var victimPath = Path.Combine(tempRoot, "victim");
            var junctionPath = Path.Combine(tempRoot, "junction");
            Directory.CreateDirectory(victimPath);
            File.WriteAllText(Path.Combine(victimPath, "sentinel.txt"), "keep");
            CreateDirectoryJunctionOrSkip(junctionPath, victimPath);

            var command = string.Join(" ; ",
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action uninstall -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -NonInteractive -Force -OutputJson"),
                "Remove-PathIfExists -Path '" + junctionPath.Replace("'", "''") + "'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            Directory.Exists(junctionPath).Should().BeTrue();
            File.Exists(Path.Combine(victimPath, "sentinel.txt")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void MoveInstallerPathWithRetry_ShouldRejectReparsePointDestinationBeforeRemoval()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "source");
            var victimPath = Path.Combine(tempRoot, "victim");
            var destinationPath = Path.Combine(tempRoot, "destination");
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(victimPath);
            File.WriteAllText(Path.Combine(sourcePath, "payload.txt"), "source");
            File.WriteAllText(Path.Combine(victimPath, "sentinel.txt"), "keep");
            CreateDirectoryJunctionOrSkip(destinationPath, victimPath);

            var actionsScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1");
            var command = string.Join(" ; ",
            [
                ". '" + actionsScriptPath.Replace("'", "''") + "'",
                "Move-InstallerPathWithRetry -SourcePath '" + sourcePath.Replace("'", "''") + "' -DestinationPath '" + destinationPath.Replace("'", "''") + "'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            Directory.Exists(sourcePath).Should().BeTrue();
            Directory.Exists(destinationPath).Should().BeTrue();
            File.Exists(Path.Combine(victimPath, "sentinel.txt")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void MoveInstallerPathWithRetry_ShouldNotProbeRejectedUncPathsInCatchBlock()
    {
        var actionsScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1");
        var command = string.Join(" ; ",
        [
            ". '" + actionsScriptPath.Replace("'", "''") + "'",
            "function Test-Path { param([string]$LiteralPath, [string]$Path) throw 'untrusted Test-Path probe' }",
            "Move-InstallerPathWithRetry -SourcePath '\\\\server\\share\\source' -DestinationPath 'C:\\Local\\destination'"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("local path");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void HelperInstallerStateSave_ShouldRejectReparsePointStateRootBeforeWriting()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appDataRoot = Path.Combine(tempRoot, "AppData", "Roaming");
            var victimPath = Path.Combine(tempRoot, "victim");
            var stateRoot = Path.Combine(appDataRoot, "WpfDevToolsMcp");
            Directory.CreateDirectory(appDataRoot);
            Directory.CreateDirectory(victimPath);
            CreateDirectoryJunctionOrSkip(stateRoot, victimPath);

            var stateScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.State.ps1");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appDataRoot.Replace("'", "''") + "'",
                ". '" + stateScriptPath.Replace("'", "''") + "'",
                "Save-InstallerState -State (Get-EmptyInstallerState)"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            Directory.EnumerateFileSystemEntries(victimPath).Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneInstallerStateSave_ShouldRejectReparsePointStateRootBeforeWriting()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appDataRoot = Path.Combine(tempRoot, "AppData", "Roaming");
            var victimPath = Path.Combine(tempRoot, "victim");
            var stateRoot = Path.Combine(appDataRoot, "WpfDevToolsMcp");
            Directory.CreateDirectory(appDataRoot);
            Directory.CreateDirectory(victimPath);
            CreateDirectoryJunctionOrSkip(stateRoot, victimPath);

            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appDataRoot.Replace("'", "''") + "'",
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action install -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -NonInteractive"),
                "Save-StandaloneInstallerState -State (Get-StandaloneEmptyInstallerState)"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            Directory.EnumerateFileSystemEntries(victimPath).Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneInstallerStateRead_ShouldNotProbeRejectedUncAppData()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var command = string.Join(" ; ",
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action uninstall -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -NonInteractive"),
                "$env:APPDATA='\\\\server\\share'",
                "function Test-Path { param([string]$LiteralPath, [string]$Path) throw 'untrusted Test-Path probe' }",
                "Get-StandaloneInstallerState | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("local path");
            result.Stderr.Should().NotContain("untrusted Test-Path probe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneInstallerStateRead_ShouldRejectReparsePointStateRootBeforeQuarantine()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appDataRoot = Path.Combine(tempRoot, "AppData", "Roaming");
            var victimPath = Path.Combine(tempRoot, "victim");
            var stateRoot = Path.Combine(appDataRoot, "WpfDevToolsMcp");
            Directory.CreateDirectory(appDataRoot);
            Directory.CreateDirectory(victimPath);
            File.WriteAllText(Path.Combine(victimPath, "installer-state.json"), "{ invalid json");
            CreateDirectoryJunctionOrSkip(stateRoot, victimPath);

            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appDataRoot.Replace("'", "''") + "'",
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action uninstall -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -NonInteractive"),
                "Get-StandaloneInstallerState | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            File.Exists(Path.Combine(victimPath, "installer-state.json")).Should().BeTrue();
            Directory.EnumerateFiles(victimPath, "*.corrupt-*", SearchOption.TopDirectoryOnly).Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallPackagePayload_ShouldRejectReparsePointInstallBaseBeforeWritingCurrentPayload()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var victimPath = Path.Combine(tempRoot, "victim");
            var packageDirectory = Path.Combine(tempRoot, "package");
            var packageBinDirectory = Path.Combine(packageDirectory, "bin");
            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(victimPath);
            Directory.CreateDirectory(packageBinDirectory);
            File.WriteAllText(Path.Combine(packageBinDirectory, "wpf-devtools-x64.exe"), "payload");
            CreateDirectoryJunctionOrSkip(installBase, victimPath);

            var actionsScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1");
            var command = string.Join(" ; ",
            [
                ". '" + actionsScriptPath.Replace("'", "''") + "'",
                "function Resolve-AbsoluteDirectory { param([string]$Path) $resolved = Assert-InstallerLocalPathTrusted -Path $Path; New-Item -ItemType Directory -Force -Path $resolved | Out-Null; Assert-InstallerLocalPathTrusted -Path $resolved | Out-Null; return $resolved }",
                "function Resolve-PackageExecutable { param([string]$PackageDirectory, [string]$ResolvedArchitecture) return (Join-Path $PackageDirectory 'bin\\wpf-devtools-x64.exe') }",
                "function Assert-PackagePayloadIntegrity { }",
                "function New-ClientRegistrationArtifacts { }",
                "$manifest = [pscustomobject]@{ channel='test'; buildConfiguration='Debug'; signaturePolicy='Skip' }",
                "Install-PackagePayload -PackageDirectory '" + packageDirectory.Replace("'", "''") + "' -PackageManifest $manifest -ResolvedArchitecture x64 -ResolvedInstallRoot '" + installRoot.Replace("'", "''") + "' -ResolvedVersion '1.0.0'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            Directory.Exists(Path.Combine(victimPath, "current")).Should().BeFalse();
            File.Exists(Path.Combine(victimPath, "install-manifest.json")).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void HelperJsonConfigRegistration_ShouldRejectReparsePointConfigParentBeforeWriting()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var configParent = Path.Combine(tempRoot, "config");
            var victimPath = Path.Combine(tempRoot, "victim");
            Directory.CreateDirectory(victimPath);
            CreateDirectoryJunctionOrSkip(configParent, victimPath);

            var registrationScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1");
            var command = string.Join(" ; ",
            [
                ". '" + registrationScriptPath.Replace("'", "''") + "'",
                "Set-JsonConfigRegistration -ClientName vscode -CollectionName servers -ConfigPath '" + Path.Combine(configParent, "mcp.json").Replace("'", "''") + "' -InstalledExecutable 'C:\\Tools\\wpf-devtools-x64.exe'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            File.Exists(Path.Combine(victimPath, "mcp.json")).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void HelperJsonConfigRegistration_ShouldRejectHardlinkedConfigBeforeWriting()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempRoot, "mcp.json");
            var victimPath = Path.Combine(tempRoot, "victim.json");
            File.WriteAllText(victimPath, "{ \"servers\": {} }");
            CreateHardLinkOrSkip(configPath, victimPath);

            var registrationScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1");
            var command = string.Join(" ; ",
            [
                ". '" + registrationScriptPath.Replace("'", "''") + "'",
                "Set-JsonConfigRegistration -ClientName vscode -CollectionName servers -ConfigPath '" + configPath.Replace("'", "''") + "' -InstalledExecutable 'C:\\Tools\\wpf-devtools-x64.exe'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("multiple hard links");
            File.ReadAllText(victimPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void NewClientRegistrationArtifacts_ShouldRejectHardlinkedArtifactBeforeWriting()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installBase = Path.Combine(tempRoot, "install-root", "x64");
            var registrationDir = Path.Combine(installBase, "client-registration");
            Directory.CreateDirectory(registrationDir);
            var artifactPath = Path.Combine(registrationDir, "vscode.json");
            var victimPath = Path.Combine(tempRoot, "victim.json");
            File.WriteAllText(victimPath, "{}");
            CreateHardLinkOrSkip(artifactPath, victimPath);

            var registrationScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1");
            var command = string.Join(" ; ",
            [
                ". '" + registrationScriptPath.Replace("'", "''") + "'",
                "New-ClientRegistrationArtifacts -InstallBase '" + installBase.Replace("'", "''") + "' -InstalledExecutable 'C:\\Tools\\wpf-devtools-x64.exe'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("multiple hard links");
            File.ReadAllText(victimPath).Should().Be("{}");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneJsonConfigRegistration_ShouldRejectReparsePointConfigParentBeforeRemoval()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var configParent = Path.Combine(tempRoot, "config");
            var victimPath = Path.Combine(tempRoot, "victim");
            var victimConfigPath = Path.Combine(victimPath, "mcp.json");
            Directory.CreateDirectory(victimPath);
            File.WriteAllText(victimConfigPath, "{ \"servers\": { \"wpf-devtools\": { \"type\": \"stdio\" } } }");
            CreateDirectoryJunctionOrSkip(configParent, victimPath);

            var command = string.Join(" ; ",
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action uninstall -Architecture x64 -Client vscode -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -NonInteractive"),
                "Remove-StandaloneJsonConfigRegistration -CollectionName servers -ConfigPath '" + Path.Combine(configParent, "mcp.json").Replace("'", "''") + "'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("reparse point");
            File.ReadAllText(victimConfigPath).Should().Contain("wpf-devtools");
            Directory.EnumerateFiles(victimPath, "*.bak-*", SearchOption.TopDirectoryOnly).Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void HelperOwnershipDiscovery_ShouldIgnoreRejectedUncExecutableWithoutProbing()
    {
        var stateScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.State.ps1");
        var command = string.Join(" ; ",
        [
            ". '" + stateScriptPath.Replace("'", "''") + "'",
            "function Test-Path { param([string]$LiteralPath, [string]$Path) throw 'untrusted Test-Path probe' }",
            "$result = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable '\\\\server\\share\\current\\bin\\wpf-devtools-x64.exe'",
            "$result | ConvertTo-Json -Compress"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
        using var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("InstallerOwned").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void StandaloneOwnershipDiscovery_ShouldIgnoreRejectedUncExecutableWithoutProbing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var command = string.Join(" ; ",
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action uninstall -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -NonInteractive"),
                "function Test-Path { param([string]$LiteralPath, [string]$Path) throw 'untrusted Test-Path probe' }",
                "$result = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable '\\\\server\\share\\current\\bin\\wpf-devtools-x64.exe'",
                "$result | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stderr.Should().NotContain("untrusted Test-Path probe");
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("InstallerOwned").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void RequireWindowsJunctions()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("Directory junction contract is Windows-specific.");
        }
    }

    private static void CreateDirectoryJunctionOrSkip(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + junctionPath + "\" \"" + targetPath + "\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        process!.WaitForExit(5000).Should().BeTrue("mklink should complete promptly");
        if (process.ExitCode != 0)
        {
            throw SkipException.ForSkip(
                "Directory junction creation failed: " + process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
        }
    }

    private static void CreateHardLinkOrSkip(string hardLinkPath, string existingFilePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("Hardlink contract is Windows-specific.");
        }

        try
        {
            if (!CreateHardLink(hardLinkPath, existingFilePath, nint.Zero))
            {
                throw SkipException.ForSkip("Hardlink creation failed with Win32 error " + Marshal.GetLastWin32Error() + ".");
            }
        }
        catch (DllNotFoundException ex)
        {
            throw SkipException.ForSkip("Hardlink creation is unavailable: " + ex.Message);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw SkipException.ForSkip("Hardlink creation is unavailable: " + ex.Message);
        }
    }
}