using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerProcessLifecycleTests
{
    [Fact]
    public void ReleaseScriptTestHarness_RunPowerShellCommand_ShouldTimeoutAndKillProcessTree()
        => AssertTimedOutProcessTreeIsTerminated(forceTaskKillFallback: false);

    [Fact]
    public void ReleaseScriptTestHarness_RunPowerShellCommand_WhenManagedKillFails_ShouldFallbackToTaskKillAndKillProcessTree()
        => AssertTimedOutProcessTreeIsTerminated(forceTaskKillFallback: true);

    private static void AssertTimedOutProcessTreeIsTerminated(bool forceTaskKillFallback)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        var originalForceTaskKillFallback = ReleaseScriptTestHarness.ForceTaskKillFallbackForTesting;
        ReleaseScriptTestHarness.ForceTaskKillFallbackForTesting = forceTaskKillFallback;
        try
        {
            var parentPidPath = Path.Combine(tempRoot, "timed-out-parent.pid");
            var childPidPath = Path.Combine(tempRoot, "timed-out-child.pid");
            var grandchildPidPath = Path.Combine(tempRoot, "timed-out-grandchild.pid");
            var readyPath = Path.Combine(tempRoot, "process-tree.ready");
            var childScriptPath = Path.Combine(tempRoot, "hang-child.ps1");
            File.WriteAllText(
                childScriptPath,
                "param([string]$ChildPidPath, [string]$GrandchildPidPath)\n" +
                "$PID | Set-Content -Path $ChildPidPath\n" +
                "$grandchild = Start-Process powershell.exe -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 60' -PassThru\n" +
                "$grandchild.Id | Set-Content -Path $GrandchildPidPath\n" +
                "Start-Sleep -Seconds 60\n");

            var command = string.Join(" ; ",
            [
                "$parentPidPath = '" + parentPidPath.Replace("'", "''") + "'",
                "$childPidPath = '" + childPidPath.Replace("'", "''") + "'",
                "$grandchildPidPath = '" + grandchildPidPath.Replace("'", "''") + "'",
                "$readyPath = '" + readyPath.Replace("'", "''") + "'",
                "$childScriptPath = '" + childScriptPath.Replace("'", "''") + "'",
                "$PID | Set-Content -Path $parentPidPath",
                "$child = Start-Process powershell.exe -ArgumentList '-NoProfile', '-File', $childScriptPath, '-ChildPidPath', $childPidPath, '-GrandchildPidPath', $grandchildPidPath -PassThru",
                "while (!(Test-Path $childPidPath) -or !(Test-Path $grandchildPidPath)) { Start-Sleep -Milliseconds 50 }",
                "Set-Content -Path $readyPath -Value ready",
                "Start-Sleep -Seconds 60"
            ]);

            var act = () => ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(10));

            act.Should().Throw<TimeoutException>();
            File.Exists(readyPath).Should().BeTrue(
                "the timeout regression should exercise a fully established process tree instead of racing process startup");
            WaitUntil(
                () => File.Exists(parentPidPath) && File.Exists(childPidPath) && File.Exists(grandchildPidPath),
                TimeSpan.FromSeconds(5),
                "Timed out waiting for timeout regression pid files to be written.");

            var processIds = new[]
            {
                int.Parse(File.ReadAllText(parentPidPath).Trim()),
                int.Parse(File.ReadAllText(childPidPath).Trim()),
                int.Parse(File.ReadAllText(grandchildPidPath).Trim())
            };

            WaitUntil(
                () => processIds.All(pid => !IsProcessRunning(pid)),
                TimeSpan.FromSeconds(5),
                $"Timed out waiting for process tree {string.Join(", ", processIds)} to exit after harness timeout.");
        }
        finally
        {
            ReleaseScriptTestHarness.ForceTaskKillFallbackForTesting = originalForceTaskKillFallback;
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleaseScriptTestHarness_RunPowerShellScript_WithOnlineInstallerScript_ShouldInjectWorkingRootAndIsolatedEnvironment()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "online-installer.ps1");
            File.WriteAllText(
                scriptPath,
                "param([string]$WorkingRoot)\n" +
                "New-Item -ItemType Directory -Path $WorkingRoot -Force | Out-Null\n" +
                "Set-Content -Path (Join-Path $WorkingRoot 'marker.txt') -NoNewline -Value keep\n" +
                "[ordered]@{\n" +
                "  WorkingRoot = $WorkingRoot\n" +
                "  MarkerExistsBeforeCleanup = Test-Path (Join-Path $WorkingRoot 'marker.txt')\n" +
                "  MarkerContentBeforeCleanup = [System.IO.File]::ReadAllText((Join-Path $WorkingRoot 'marker.txt'))\n" +
                "  UserProfile = $env:USERPROFILE\n" +
                "  AppData = $env:APPDATA\n" +
                "  LocalAppData = $env:LOCALAPPDATA\n" +
                "  Temp = $env:TEMP\n" +
                "  Tmp = $env:TMP\n" +
                "} | ConvertTo-Json -Compress\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                Array.Empty<string>(),
                timeout: TimeSpan.FromSeconds(5));

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stderr.Should().BeEmpty();
            using var payload = JsonDocument.Parse(result.Stdout);
            var root = payload.RootElement;
            var userProfile = root.GetProperty("UserProfile").GetString();
            var appData = root.GetProperty("AppData").GetString();
            var localAppData = root.GetProperty("LocalAppData").GetString();
            var tempPath = root.GetProperty("Temp").GetString();
            var tmpPath = root.GetProperty("Tmp").GetString();
            var workingRoot = root.GetProperty("WorkingRoot").GetString();

            userProfile.Should().NotBeNullOrWhiteSpace();
            var resolvedUserProfile = userProfile!;
            root.GetProperty("MarkerExistsBeforeCleanup").GetBoolean().Should().BeTrue();
            root.GetProperty("MarkerContentBeforeCleanup").GetString().Should().Be("keep");
            appData.Should().Be(Path.Combine(resolvedUserProfile, "AppData", "Roaming"));
            localAppData.Should().Be(Path.Combine(resolvedUserProfile, "AppData", "Local"));
            tempPath.Should().Be(Path.Combine(resolvedUserProfile, "Temp"));
            tmpPath.Should().Be(tempPath);
            workingRoot.Should().StartWith(resolvedUserProfile);
            Path.GetFileName(resolvedUserProfile).Should().StartWith("e", "owned harness environment roots use the short temp prefix");
            resolvedUserProfile.Length.Should().BeLessThan(160, "offline package extraction must stay below Windows PowerShell 5.1 path limits on CI");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleaseScriptTestHarness_RunPowerShellScript_WithSharedEnvironmentOverrides_ShouldReuseCallerRootWithoutDeletingIt()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sharedRoot = Path.Combine(tempRoot, "caller-root");
            var userProfile = Path.Combine(sharedRoot, "UserProfile");
            var appData = Path.Combine(sharedRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(sharedRoot, "AppData", "Local");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            var sentinelPath = Path.Combine(appData, "sentinel.txt");
            File.WriteAllText(sentinelPath, "keep");

            var scriptPath = Path.Combine(tempRoot, "online-installer.ps1");
            File.WriteAllText(
                scriptPath,
                "param([string]$WorkingRoot)\n" +
                "New-Item -ItemType Directory -Path $WorkingRoot -Force | Out-Null\n" +
                "Set-Content -Path (Join-Path $WorkingRoot 'marker.txt') -NoNewline -Value keep\n" +
                "[ordered]@{\n" +
                "  WorkingRoot = $WorkingRoot\n" +
                "  MarkerExistsBeforeCleanup = Test-Path (Join-Path $WorkingRoot 'marker.txt')\n" +
                "  MarkerContentBeforeCleanup = [System.IO.File]::ReadAllText((Join-Path $WorkingRoot 'marker.txt'))\n" +
                "  UserProfile = $env:USERPROFILE\n" +
                "  AppData = $env:APPDATA\n" +
                "  LocalAppData = $env:LOCALAPPDATA\n" +
                "} | ConvertTo-Json -Compress\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                Array.Empty<string>(),
                new Dictionary<string, string?>
                {
                    ["USERPROFILE"] = userProfile,
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData
                },
                timeout: TimeSpan.FromSeconds(5));

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stderr.Should().BeEmpty();
            using var payload = JsonDocument.Parse(result.Stdout);
            var root = payload.RootElement;
            var workingRoot = root.GetProperty("WorkingRoot").GetString();
            var expectedWorkingRootParent = Path.Combine(sharedRoot, "wr");

            root.GetProperty("UserProfile").GetString().Should().Be(userProfile);
            root.GetProperty("AppData").GetString().Should().Be(appData);
            root.GetProperty("LocalAppData").GetString().Should().Be(localAppData);
            root.GetProperty("MarkerExistsBeforeCleanup").GetBoolean().Should().BeTrue();
            root.GetProperty("MarkerContentBeforeCleanup").GetString().Should().Be("keep");
            workingRoot.Should().StartWith(expectedWorkingRootParent);
            Path.GetDirectoryName(workingRoot!).Should().Be(expectedWorkingRootParent);
            Directory.Exists(sharedRoot).Should().BeTrue("shared caller-owned environment roots must be preserved");
            File.Exists(sentinelPath).Should().BeTrue("shared caller-owned state should survive harness cleanup");
            Directory.Exists(workingRoot).Should().BeFalse("the harness should still clean up its injected working root inside the shared caller root after the script creates content there");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleaseScriptTestHarness_RunPowerShellScript_WithMismatchedUserProfileOverride_ShouldUseOwnedIsolatedWorkingRootAndTemp()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sharedRoot = Path.Combine(tempRoot, "caller-root");
            var userProfileRoot = Path.Combine(tempRoot, "separate-user-root");
            var userProfile = Path.Combine(userProfileRoot, "UserProfile");
            var appData = Path.Combine(sharedRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(sharedRoot, "AppData", "Local");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            var sentinelPath = Path.Combine(appData, "sentinel.txt");
            File.WriteAllText(sentinelPath, "keep");

            var scriptPath = Path.Combine(tempRoot, "online-installer.ps1");
            File.WriteAllText(
                scriptPath,
                "param([string]$WorkingRoot)\n" +
                "New-Item -ItemType Directory -Path $WorkingRoot -Force | Out-Null\n" +
                "Set-Content -Path (Join-Path $WorkingRoot 'marker.txt') -NoNewline -Value keep\n" +
                "[ordered]@{\n" +
                "  WorkingRoot = $WorkingRoot\n" +
                "  MarkerExistsBeforeCleanup = Test-Path (Join-Path $WorkingRoot 'marker.txt')\n" +
                "  MarkerContentBeforeCleanup = [System.IO.File]::ReadAllText((Join-Path $WorkingRoot 'marker.txt'))\n" +
                "  UserProfile = $env:USERPROFILE\n" +
                "  AppData = $env:APPDATA\n" +
                "  LocalAppData = $env:LOCALAPPDATA\n" +
                "  Temp = $env:TEMP\n" +
                "  Tmp = $env:TMP\n" +
                "} | ConvertTo-Json -Compress\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                Array.Empty<string>(),
                new Dictionary<string, string?>
                {
                    ["USERPROFILE"] = userProfile,
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData
                },
                timeout: TimeSpan.FromSeconds(5));

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stderr.Should().BeEmpty();
            using var payload = JsonDocument.Parse(result.Stdout);
            var root = payload.RootElement;
            var workingRoot = root.GetProperty("WorkingRoot").GetString();
            var tempPath = root.GetProperty("Temp").GetString();
            var tmpPath = root.GetProperty("Tmp").GetString();

            root.GetProperty("UserProfile").GetString().Should().Be(userProfile);
            root.GetProperty("AppData").GetString().Should().Be(appData);
            root.GetProperty("LocalAppData").GetString().Should().Be(localAppData);
            root.GetProperty("MarkerExistsBeforeCleanup").GetBoolean().Should().BeTrue();
            root.GetProperty("MarkerContentBeforeCleanup").GetString().Should().Be("keep");
            workingRoot.Should().NotStartWith(sharedRoot, "a mismatched USERPROFILE must prevent shared-root reuse");
            workingRoot.Should().NotStartWith(userProfileRoot, "a mismatched USERPROFILE must fall back to a harness-owned environment root");
            tempPath.Should().NotStartWith(sharedRoot, "TEMP should come from the owned isolated environment root when the shared-root tuple is inconsistent");
            tempPath.Should().NotStartWith(userProfileRoot, "TEMP should not reuse the mismatched USERPROFILE root");
            tmpPath.Should().Be(tempPath);
            Directory.Exists(sharedRoot).Should().BeTrue("caller-owned APPDATA roots must be preserved when shared-root reuse is rejected");
            File.Exists(sentinelPath).Should().BeTrue("caller-owned APPDATA state should survive harness cleanup when shared-root reuse is rejected");
            Directory.Exists(workingRoot!).Should().BeFalse("the harness should clean up its owned working root after the script creates content there");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_TuiTestMode_ShouldFailFastWhenKeyQueueIsExhausted()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(30));

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("TUI test key queue exhausted");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiFlow_ShouldDeclareLatestVersionRefreshTeardown()
    {
        var flowContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));
        var installerContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        flowContent.Should().Contain("Stop-TuiLatestVersionRefreshCore");
        installerContent.Should().Contain("Stop-LatestInstallerVersionRefresh");
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }

        condition().Should().BeTrue(failureMessage);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
