using System.IO;
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("PackagingIntegration")]
public sealed class ReleasePackagingTestHarnessIntegrationTests
{
    [Fact]
    public void RunPowerShellScript_WithOnlineInstallerScript_ShouldInjectWorkingRootAndIsolatedEnvironment()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
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

            var result = ReleasePackagingTestHarness.RunPowerShellScript(
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
            resolvedUserProfile.Should().StartWith(ReleasePackagingTestHarness.GetRepoFilePath("tmp"));
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RunPowerShellScript_WithSharedEnvironmentOverrides_ShouldReuseCallerRootWithoutDeletingIt()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
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

            var result = ReleasePackagingTestHarness.RunPowerShellScript(
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
            var expectedWorkingRootParent = Path.Combine(sharedRoot, "wpf-devtools-working-root");

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
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RunPowerShellScript_WithMismatchedUserProfileOverride_ShouldUseOwnedIsolatedWorkingRootAndTemp()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
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

            var result = ReleasePackagingTestHarness.RunPowerShellScript(
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
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RunPowerShellScript_WithSharedEnvironmentOverridesForNonInstallerScript_ShouldNotReuseCallerRootForTemp()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var sharedRoot = Path.Combine(tempRoot, "caller-root");
            var userProfile = Path.Combine(sharedRoot, "UserProfile");
            var appData = Path.Combine(sharedRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(sharedRoot, "AppData", "Local");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);

            var scriptPath = Path.Combine(tempRoot, "echo-temp.ps1");
            File.WriteAllText(
                scriptPath,
                "[ordered]@{\n" +
                "  Temp = $env:TEMP\n" +
                "  UserProfile = $env:USERPROFILE\n" +
                "  AppData = $env:APPDATA\n" +
                "  LocalAppData = $env:LOCALAPPDATA\n" +
                "} | ConvertTo-Json -Compress\n");

            var result = ReleasePackagingTestHarness.RunPowerShellScript(
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
            using var payload = JsonDocument.Parse(result.Stdout);
            var root = payload.RootElement;
            var tempPath = root.GetProperty("Temp").GetString();

            root.GetProperty("UserProfile").GetString().Should().Be(userProfile);
            root.GetProperty("AppData").GetString().Should().Be(appData);
            root.GetProperty("LocalAppData").GetString().Should().Be(localAppData);
            tempPath.Should().NotStartWith(sharedRoot, "non-installer scripts should not reuse the caller-owned shared root as the harness environment root");
            tempPath.Should().StartWith(ReleasePackagingTestHarness.GetRepoFilePath("tmp"));
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RunPowerShellScript_WhenProcessTimesOut_ShouldTerminateChildProcessTree()
        => AssertTimedOutProcessTreeIsTerminated(forceTaskKillFallback: false);

    [Fact]
    public void RunPowerShellScript_WhenManagedKillFails_ShouldFallbackToTaskKillAndTerminateChildProcessTree()
        => AssertTimedOutProcessTreeIsTerminated(forceTaskKillFallback: true);

    private static void AssertTimedOutProcessTreeIsTerminated(bool forceTaskKillFallback)
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        var originalForceTaskKillFallback = ReleasePackagingTestHarness.ForceTaskKillFallbackForTesting;
        ReleasePackagingTestHarness.ForceTaskKillFallbackForTesting = forceTaskKillFallback;
        try
        {
            var parentPidPath = Path.Combine(tempRoot, "parent.pid");
            var childPidPath = Path.Combine(tempRoot, "child.pid");
            var grandchildPidPath = Path.Combine(tempRoot, "grandchild.pid");
            var childScriptPath = Path.Combine(tempRoot, "hang-child.ps1");
            File.WriteAllText(
                childScriptPath,
                "param([string]$ChildPidPath, [string]$GrandchildPidPath)\n" +
                "$PID | Set-Content -Path $ChildPidPath\n" +
                "$grandchild = Start-Process powershell.exe -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 60' -PassThru\n" +
                "$grandchild.Id | Set-Content -Path $GrandchildPidPath\n" +
                "Start-Sleep -Seconds 60\n");

            var scriptPath = Path.Combine(tempRoot, "hang.ps1");
            File.WriteAllText(
                scriptPath,
                "param([string]$ParentPidPath, [string]$ChildPidPath, [string]$GrandchildPidPath, [string]$ChildScriptPath)\n" +
                "$PID | Set-Content -Path $ParentPidPath\n" +
                "$child = Start-Process powershell.exe -ArgumentList '-NoProfile', '-File', $ChildScriptPath, '-ChildPidPath', $ChildPidPath, '-GrandchildPidPath', $GrandchildPidPath -PassThru\n" +
                "while (!(Test-Path $ChildPidPath) -or !(Test-Path $GrandchildPidPath)) { Start-Sleep -Milliseconds 50 }\n" +
                "Start-Sleep -Seconds 60\n");

            Action act = () => ReleasePackagingTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-ParentPidPath", parentPidPath,
                    "-ChildPidPath", childPidPath,
                    "-GrandchildPidPath", grandchildPidPath,
                    "-ChildScriptPath", childScriptPath
                ],
                timeout: TimeSpan.FromSeconds(3));

            act.Should().Throw<TimeoutException>();
            ConditionWaiter.WaitUntil(
                () => File.Exists(parentPidPath) && File.Exists(childPidPath) && File.Exists(grandchildPidPath),
                TimeSpan.FromSeconds(5),
                "Timed out waiting for timeout regression pid files to be written.",
                TimeSpan.FromMilliseconds(100));

            var processIds = new[]
            {
                int.Parse(File.ReadAllText(parentPidPath).Trim()),
                int.Parse(File.ReadAllText(childPidPath).Trim()),
                int.Parse(File.ReadAllText(grandchildPidPath).Trim())
            };

            ConditionWaiter.WaitUntil(
                () => processIds.All(pid => !IsProcessRunning(pid)),
                TimeSpan.FromSeconds(5),
                $"Timed out waiting for process tree {string.Join(", ", processIds)} to exit after harness timeout.",
                TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            ReleasePackagingTestHarness.ForceTaskKillFallbackForTesting = originalForceTaskKillFallback;
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
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