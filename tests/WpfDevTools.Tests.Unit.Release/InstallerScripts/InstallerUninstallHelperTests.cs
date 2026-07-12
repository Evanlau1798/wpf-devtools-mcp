using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerUninstallHelperTests
{
    [Theory]
    [InlineData("case")]
    [InlineData("slash")]
    public void InstallerOwnership_ShouldNormalizeExecutableVariantsUsingRuntimeDefinition(string variantKind)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var expected = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllText(expected, "stub");
            File.WriteAllText(
                Path.Combine(installBase, "install-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    executable = expected,
                    installRoot,
                    architecture = "x64",
                    version = "1.2.3"
                }));
            var variant = variantKind == "case"
                ? expected.ToUpperInvariant()
                : expected.Replace('\\', '/').Replace("/current/", "/CURRENT/").Replace("/bin/", "/BIN/");
            var command = string.Join(Environment.NewLine,
            [
                DotSourceInstallerHelper("Installer.Discovery.ps1"),
                DotSourceInstallerHelper("Installer.State.Installation.ps1"),
                "$ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable '" + Escape(variant) + "'",
                "$ownership | ConvertTo-Json -Compress"
            ]);

            var result = RunDefinitionCommand(tempRoot, command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("InstallerOwned").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("InstallRoot").GetString().Should().Be(installRoot);
            json.RootElement.GetProperty("Architecture").GetString().Should().Be("x64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData("global")]
    [InlineData("project")]
    public void CursorProfile_ShouldRejectHostileRecordedTargetAndUseRequestedSafePath(string cursorMode)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var hostileTargetPath = Path.Combine(tempRoot, "hostile", cursorMode, "mcp.json");
            var projectRoot = Path.Combine(tempRoot, "project");
            var safeConfigPath = cursorMode == "global"
                ? Path.Combine(tempRoot, "safe", "global", "mcp.json")
                : Path.Combine(projectRoot, ".cursor", "mcp.json");
            var selectedClient = "cursor-" + cursorMode;
            var preludeArguments = "-Action uninstall -Architecture x64 -Client cursor -CursorMode " + cursorMode
                + (cursorMode == "global" ? " -CursorConfigPath '" + Escape(safeConfigPath) + "'" : string.Empty)
                + " -CursorProjectRoot '" + Escape(projectRoot) + "' -InstallRoot '"
                + Escape(Path.Combine(tempRoot, "install-root")) + "' -NonInteractive -OutputJson";
            var command = string.Join(Environment.NewLine,
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(preludeArguments),
                DotSourceInstallerHelper("Installer.State.Installation.ps1"),
                DotSourceInstallerHelper("Installer.Registration.TrustedTargets.ps1"),
                DotSourceInstallerHelper("Installer.Registration.Cursor.ps1"),
                "$record = [ordered]@{ target='" + Escape(hostileTargetPath) + "'; mode='" + selectedClient + "' }",
                "$trusted = Get-TrustedCursorRecordedTarget -SelectedClient '" + selectedClient + "' -RegistrationRecord $record",
                "$profile = Resolve-CursorRegistrationProfile -SelectedClient '" + selectedClient + "' -RegistrationRecord $record",
                "@{ TrustedIsNull = ($null -eq $trusted); Mode = [string]$profile.Mode; ConfigPath = [string]$profile.ConfigPath } | ConvertTo-Json -Compress"
            ]);

            var result = RunDefinitionCommand(tempRoot, command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("TrustedIsNull").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("Mode").GetString().Should().Be(cursorMode);
            json.RootElement.GetProperty("ConfigPath").GetString().Should().Be(safeConfigPath);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData("cursor-global")]
    [InlineData("cursor-project")]
    public void CursorManifestTarget_WhenStateIsMissing_ShouldRecoverRegistrationTarget(string stateKey)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var installedExecutable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            var expectedTarget = Path.Combine(tempRoot, "managed-targets", stateKey, "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(installedExecutable)!);
            File.WriteAllText(installedExecutable, "stub");
            File.WriteAllText(
                Path.Combine(installBase, "install-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    executable = installedExecutable,
                    installRoot,
                    architecture = "x64",
                    version = "1.2.3",
                    managedRegistrationTargets = new Dictionary<string, string>
                    {
                        [stateKey] = expectedTarget
                    }
                }));
            var preludeArguments = "-Action full-uninstall -Architecture x64 -Client other -InstallRoot '"
                + Escape(installRoot) + "' -NonInteractive -Force -OutputJson";
            var resolveTarget = "$target = Get-TrustedCursorManifestTarget -SelectedClient '" + stateKey + "' -RegistrationRecord $record";
            var command = string.Join(Environment.NewLine,
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(preludeArguments),
                DotSourceInstallerHelper("Installer.State.Installation.ps1"),
                DotSourceInstallerHelper("Installer.Registration.TrustedTargets.ps1"),
                DotSourceInstallerHelper("Installer.Registration.Cursor.ps1"),
                "$record = [ordered]@{ installedExecutable='" + Escape(installedExecutable) + "'; installRoot='"
                    + Escape(installRoot) + "'; architecture='x64' }",
                resolveTarget,
                "$target | ConvertTo-Json -Compress"
            ]);

            var result = RunDefinitionCommand(tempRoot, command);

            result.ExitCode.Should().Be(0, result.Stderr);
            JsonSerializer.Deserialize<string>(result.Stdout.Trim()).Should().Be(expectedTarget);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunDefinitionCommand(
        string tempRoot,
        string command)
        => ReleaseScriptTestHarness.RunPowerShellCommand(
            command,
            new Dictionary<string, string?>
            {
                ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                ["WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer")
            });

    private static string Escape(string value)
        => OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(value);

    private static string DotSourceInstallerHelper(string fileName)
        => ". '" + Escape(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/" + fileName)) + "'";
}
