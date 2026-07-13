using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class OnlineInstallerPlanModeTests
{
    [Fact]
    public void OnlineInstaller_PlanMode_ShouldEmitReadOnlyMachineReadablePlan()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var workingRoot = Path.Combine(tempRoot, "working-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "plan",
                    "-Architecture", "x86",
                    "-InstallRoot", installRoot,
                    "-WorkingRoot", workingRoot,
                    "-OutputJson",
                    "-NonInteractive"
                ],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            var root = json.RootElement;

            AssertPlanContract(root);
            root.GetProperty("action").GetString().Should().Be("plan");
            root.GetProperty("platform").GetString().Should().Be("windows");
            root.GetProperty("architecture").GetString().Should().Be("x86");
            root.GetProperty("installRootDefault").GetString().Should().Be(installRoot);
            root.GetProperty("preferredInstallRoot").GetString().Should().Be(installRoot);
            root.GetProperty("fallbackInstallRoot").GetString().Should().Be(installRoot);
            root.GetProperty("installRootSource").GetString().Should().Be("explicit");
            root.GetProperty("requiresUserConfirmationBeforeMutation").GetBoolean().Should().BeTrue();
            root.GetProperty("mutatesFileSystem").GetBoolean().Should().BeFalse();
            root.GetProperty("downloadsReleaseAssets").GetBoolean().Should().BeFalse();
            root.GetProperty("runsClientRegistration").GetBoolean().Should().BeFalse();

            root.GetProperty("supportedClients").EnumerateArray()
                .Select(element => element.GetString())
                .Should().Equal("claude-code", "codex", "grok", "cursor", "vscode", "visual-studio", "claude-desktop", "other");

            root.GetProperty("detectedClients").EnumerateArray()
                .Should().Contain(element =>
                    element.GetProperty("client").GetString() == "other" &&
                    element.GetProperty("available").GetBoolean());

            Directory.Exists(installRoot).Should().BeFalse("plan mode must not create the install root");
            Directory.Exists(workingRoot).Should().BeFalse("plan mode must not create the working root");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_ShouldSurviveMissingProfileEnvironmentWithoutExplicitInstallRoot()
    {
        var command = string.Join("; ",
            "$ErrorActionPreference = 'Stop'",
            "Remove-Item Env:APPDATA -ErrorAction SilentlyContinue",
            "Remove-Item Env:LOCALAPPDATA -ErrorAction SilentlyContinue",
            "Remove-Item Env:USERPROFILE -ErrorAction SilentlyContinue",
            "$env:PATH = ''",
            "& .\\scripts\\online-installer.ps1 -Action plan -Architecture x64 -OutputJson -NonInteractive");

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        var root = json.RootElement;
        AssertPlanContract(root);
        root.GetProperty("client").GetString().Should().Be("other");
        root.GetProperty("installRootSource").GetString().Should().Be("default");
        root.GetProperty("installRootDefault").GetString().Should().EndWith("WpfDevToolsMcp");
        root.GetProperty("preferredInstallRoot").GetString().Should().Be(root.GetProperty("installRootDefault").GetString());
        root.GetProperty("fallbackInstallRoot").GetString().Should().Be(root.GetProperty("installRootDefault").GetString());
    }

    [Fact]
    public void OnlineInstaller_PlanMode_ShouldSurviveMissingProfileEnvironmentWhenInstallRootIsExplicit()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var workingRoot = Path.Combine(tempRoot, "working-root");

            var command = string.Join("; ",
                "$ErrorActionPreference = 'Stop'",
                "Remove-Item Env:APPDATA -ErrorAction SilentlyContinue",
                "Remove-Item Env:LOCALAPPDATA -ErrorAction SilentlyContinue",
                "Remove-Item Env:USERPROFILE -ErrorAction SilentlyContinue",
                "$env:PATH = ''",
                "& .\\scripts\\online-installer.ps1 -Action plan -Architecture x64 -InstallRoot " + QuotePowerShellString(installRoot) + " -WorkingRoot " + QuotePowerShellString(workingRoot) + " -OutputJson -NonInteractive");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("client").GetString().Should().Be("other");
            json.RootElement.GetProperty("installRootDefault").GetString().Should().Be(installRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_WithIntegrityRuntime_ShouldNotUseNetworkOrMutateWorkingRoot()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptCopy = Path.Combine(tempRoot, "online-installer.ps1");
            var workingRoot = Path.Combine(tempRoot, "working-root");
            var installRoot = Path.Combine(tempRoot, "install-root");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), scriptCopy);
            ReleaseScriptTestHarness.CopyOnlineInstallerRuntimeBundle(tempRoot);

            var command = string.Join("; ",
                "$ErrorActionPreference = 'Stop'",
                "function Invoke-WebRequest { throw 'plan attempted web request' }",
                "& " + QuotePowerShellString(scriptCopy) + " -Action plan -Version 0.1.0 -Architecture x64 -InstallRoot " + QuotePowerShellString(installRoot) + " -WorkingRoot " + QuotePowerShellString(workingRoot) + " -OutputJson -NonInteractive");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            AssertPlanContract(json.RootElement);
            Directory.Exists(workingRoot).Should().BeFalse("plan mode must not bootstrap helper assets before user confirmation");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_WithAdjacentHelperFiles_ShouldNotDotSourceHelpers()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptCopy = Path.Combine(tempRoot, "online-installer.ps1");
            var helperRoot = Path.Combine(tempRoot, "installer");
            var installRoot = Path.Combine(tempRoot, "install-root");
            Directory.CreateDirectory(helperRoot);
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), scriptCopy);
            foreach (var helperLeafName in SharedInstallerHelperLeafNames)
            {
                File.WriteAllText(
                    Path.Combine(helperRoot, helperLeafName),
                    "throw 'plan dot-sourced helper before confirmation'");
            }

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptCopy,
                [
                    "-Action", "plan",
                    "-Version", "0.1.0",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-OutputJson",
                    "-NonInteractive"
                ]);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            AssertPlanContract(json.RootElement);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_ShouldNotMutateTrustedMetadataEnvironmentInSameProcess()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var trustedMetadata = Path.Combine(tempRoot, "metadata");
            var command = string.Join("; ",
                "$ErrorActionPreference = 'Stop'",
                "$env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY = 'preserve-me'",
                "& .\\scripts\\online-installer.ps1 -Action plan -Architecture x64 -InstallRoot " + QuotePowerShellString(installRoot) + " -TrustedReleaseMetadataDirectory " + QuotePowerShellString(trustedMetadata) + " -OutputJson | Out-Null",
                "if ($env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY -ne 'preserve-me') { throw \"trusted metadata env was mutated to '$env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY'\" }",
                "Write-Output $env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("preserve-me");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_ShouldReportPreferredLiveInstallRootWithoutMutatingState()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var previousInstallRoot = Path.Combine(tempRoot, "previous-install");
            var defaultInstallRoot = Path.Combine(appData, "WpfDevToolsMcp");
            var stateRoot = Path.Combine(appData, "WpfDevToolsMcp");
            var statePath = Path.Combine(stateRoot, "installer-state.json");
            var installBase = Path.Combine(previousInstallRoot, "x64");
            var executable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            var manifestPath = Path.Combine(installBase, "install-manifest.json");

            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            Directory.CreateDirectory(stateRoot);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(manifestPath, $$"""
            {
              "installRoot": "{{JsonEscape(previousInstallRoot)}}",
              "architecture": "x64",
              "version": "0.1.0",
              "executable": "{{JsonEscape(executable)}}"
            }
            """);
            File.WriteAllText(statePath, $$"""
            {
              "lastInstallRoot": "{{JsonEscape(previousInstallRoot)}}",
              "architectures": {},
              "registrations": {}
            }
            """);
            var beforeState = File.ReadAllText(statePath);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "plan",
                    "-Architecture", "x64",
                    "-OutputJson",
                    "-NonInteractive"
                ],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            var root = json.RootElement;
            root.GetProperty("installRootDefault").GetString().Should().Be(defaultInstallRoot);
            root.GetProperty("preferredInstallRoot").GetString().Should().Be(previousInstallRoot);
            root.GetProperty("fallbackInstallRoot").GetString().Should().Be(defaultInstallRoot);
            root.GetProperty("installRootSource").GetString().Should().Be("previous-live-install");
            File.ReadAllText(statePath).Should().Be(beforeState, "plan mode must not repair or rewrite installer state");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_ShouldRejectForgedPreviousInstallManifestEvidence()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var previousInstallRoot = Path.Combine(tempRoot, "previous-install");
            var defaultInstallRoot = Path.Combine(appData, "WpfDevToolsMcp");
            var stateRoot = Path.Combine(appData, "WpfDevToolsMcp");
            var statePath = Path.Combine(stateRoot, "installer-state.json");
            var installBase = Path.Combine(previousInstallRoot, "x64");
            var forgedExecutable = Path.Combine(tempRoot, "not-owned", "wpf-devtools-x64.exe");
            var manifestPath = Path.Combine(installBase, "install-manifest.json");

            Directory.CreateDirectory(Path.GetDirectoryName(forgedExecutable)!);
            Directory.CreateDirectory(installBase);
            Directory.CreateDirectory(stateRoot);
            File.WriteAllText(forgedExecutable, string.Empty);
            File.WriteAllText(manifestPath, $$"""
            {
              "installRoot": "{{JsonEscape(previousInstallRoot)}}",
              "architecture": "x64",
              "version": "0.1.0",
              "executable": "{{JsonEscape(forgedExecutable)}}"
            }
            """);
            File.WriteAllText(statePath, $$"""
            {
              "lastInstallRoot": "{{JsonEscape(previousInstallRoot)}}",
              "architectures": {},
              "registrations": {}
            }
            """);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "plan",
                    "-Architecture", "x64",
                    "-OutputJson",
                    "-NonInteractive"
                ],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            var root = json.RootElement;
            root.GetProperty("installRootDefault").GetString().Should().Be(defaultInstallRoot);
            root.GetProperty("preferredInstallRoot").GetString().Should().Be(defaultInstallRoot);
            root.GetProperty("fallbackInstallRoot").GetString().Should().Be(defaultInstallRoot);
            root.GetProperty("installRootSource").GetString().Should().Be("default");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_ShouldEmitJsonEvenWithoutOutputJsonSwitch()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "plan",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-NonInteractive"
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("action").GetString().Should().Be("plan");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string QuotePowerShellString(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string JsonEscape(string value)
        => value.Replace(@"\", @"\\", StringComparison.Ordinal);

    private static void AssertPlanContract(JsonElement root)
    {
        root.GetProperty("contractVersion").GetInt32().Should().Be(1);
        root.GetProperty("mutationBoundary").GetString().Should().NotBeNullOrWhiteSpace();
        foreach (var detectedClient in root.GetProperty("detectedClients").EnumerateArray())
        {
            detectedClient.GetProperty("client").GetString().Should().NotBeNullOrWhiteSpace();
            detectedClient.GetProperty("available").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
            detectedClient.GetProperty("registrationStyle").GetString().Should().NotBeNullOrWhiteSpace();
            detectedClient.GetProperty("evidence").ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    private static readonly string[] SharedInstallerHelperLeafNames =
    [
        "Installer.Discovery.ps1",
        "Installer.Discovery.Detection.ps1",
        "Installer.Uninstall.Standalone.ps1",
        "Installer.Uninstall.ps1",
        "Installer.Release.ps1",
        "Installer.PackageIntegrity.ps1",
        "Installer.Encoding.ps1",
        "Installer.State.ps1",
        "Installer.State.Installation.ps1",
        "Installer.Registration.Paths.ps1",
        "Installer.Registration.Json.ps1",
        "Installer.Registration.TrustedTargets.ps1",
        "Installer.Registration.Cursor.ps1",
        "Installer.Registration.Commands.ps1",
        "Installer.Registration.Clients.ps1",
        "Installer.Registration.ps1",
        "Installer.Verification.Commands.ps1",
        "Installer.Verification.ps1",
        "Installer.Actions.ps1"
    ];
}
