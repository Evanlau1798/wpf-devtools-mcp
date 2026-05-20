using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxArtifactPreflight_StopSmokeTarget_ShouldCleanupDescendantsWhenRootShutdownFails()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ProcessCleanup.ps1"));
        var stopBlock = runner.Substring(
            runner.IndexOf("function Stop-SmokeTarget", StringComparison.Ordinal),
            runner.Length -
            runner.IndexOf("function Stop-SmokeTarget", StringComparison.Ordinal));

        stopBlock.Should().Contain("$rootShutdownFailure");
        stopBlock.Should().Contain("$descendantCleanupFailure");
        stopBlock.Should().Contain("$rootSnapshot");
        stopBlock.Should().Contain("Stop-ProcessSnapshots -Snapshots @(@($rootSnapshot) + @($descendantSnapshots))");
        stopBlock.Should().Contain("Root shutdown failure:");
    }

    [Fact]
    public void SandboxArtifactPreflight_StopProcessSnapshots_ShouldCleanChildrenOfExitedScanRoots()
    {
        var tempRoot = CreateTempRoot();
        var token = $"late-spawn-orphan-{Guid.NewGuid():N}";
        try
        {
            var spawnerRoot = Path.Combine(tempRoot, token);
            var spawnerPath = CreateChildSpawnerExecutable(spawnerRoot, token);
            var childStartedPath = Path.Combine(spawnerRoot, "child-started.txt");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{GetSmokeTargetFunctionBootstrap(scriptPath)}}
                $parent = Start-Process -FilePath '{{EscapePowerShellPath(spawnerPath)}}' -PassThru
                $scanRoot = New-ProcessSnapshot -ProcessId $parent.Id
                if ($null -eq $scanRoot) {
                    throw 'Parent process snapshot was not captured.'
                }

                $deadline = [DateTime]::UtcNow.AddSeconds(10)
                do {
                    if ([System.IO.File]::Exists('{{EscapePowerShellPath(childStartedPath)}}')) { break }
                    Start-Sleep -Milliseconds 100
                } while ([DateTime]::UtcNow -lt $deadline)
                if (-not [System.IO.File]::Exists('{{EscapePowerShellPath(childStartedPath)}}')) {
                    throw 'Timed out waiting for late child process.'
                }

                $scanRoot.DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks
                $parent.WaitForExit(10000) | Out-Null
                if (-not $parent.HasExited) {
                    throw 'Parent process did not exit.'
                }

                Stop-ProcessSnapshots -Snapshots @() -ScanRoots @($scanRoot)
                $remaining = @(Get-CimInstance Win32_Process | Where-Object {
                    $_.ProcessId -ne $PID -and $_.CommandLine -like '*{{token}}*'
                })
                if ($remaining.Count -gt 0) {
                    throw "Late child process remained after cleanup: $($remaining.ProcessId -join ', ')"
                }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-late-spawn-orphan.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
            AssertNoProcessCommandLineContains(token);
        }
        finally
        {
            KillProcessesCommandLineContains(token);
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxArtifactPreflight_ProcessCleanup_ShouldUseIdentityScanRoots()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ProcessCleanup.ps1"));
        var preflight = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1"));

        runner.Should().NotContain("[int[]]$ScanRootIds");
        runner.Should().NotContain("-ScanRootIds");
        runner.Should().Contain("function Get-SmokeTargetProcessId");
        runner.Should().Contain("if ($processId -le 0) {");
        runner.Should().Contain("if ($ParentProcessId -le 0) {");
        runner.Should().Contain("[object[]]$ScanRoots = @()");
        runner.Should().Contain("DescendantCutoffUtcTicks");
        runner.Should().Contain("CreationCutoffUtcTicks");
        runner.Should().Contain("Update-ProcessSnapshotCutoffIfAlive");
        runner.Should().Contain("Get-MatchingProcessFromSnapshot");
        runner.Should().Contain("StartTime.ToUniversalTime().Ticks");
        runner.Should().Contain("[TimeSpan]::FromMilliseconds(1).Ticks");
        runner.Should().Contain("Get-MatchingProcessFromSnapshot -Snapshot $snapshot").And.Contain("$process.Kill()");
        runner.Should().Contain("Set-SmokeTargetRootSnapshot");
        runner.Should().Contain("Get-SmokeTargetRootSnapshot");
        runner.Should().Contain("Remove-SmokeTargetRootSnapshot").And.NotContain("Stop-Process -Id $snapshot.ProcessId");
        runner.Should().NotContain("if ($null -ne $rootSnapshot) {\r\n                $rootSnapshot.DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks");
        preflight.Should().Contain("try { Stop-SmokeTarget -Process $smokeProcess } finally { $smokeProcess = $null }");
        preflight.Should().Contain("if ($null -ne $smokeProcess) {");
    }

    [Fact]
    public void SandboxArtifactPreflight_ShouldCleanTransientGrandchildWhenStartupTimesOut()
    {
        var tempRoot = CreateTempRoot();
        var token = $"transient-grandchild-{Guid.NewGuid():N}";
        try
        {
            var smokeProjectRoot = Path.Combine(tempRoot, token);
            var smokeTargetPath = CreateTransientGrandchildSmokeTarget(smokeProjectRoot, token);
            var grandchildStartedPath = Path.Combine(smokeProjectRoot, "grandchild-started.txt");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            try {
            $ErrorActionPreference = 'Stop'
            {{GetSmokeTargetFunctionBootstrap(scriptPath)}}

            $SmokeTargetPath = '{{EscapePowerShellPath(smokeTargetPath)}}'
            $SmokeTargetStartupTimeoutSeconds = 3
            try {
                Start-SmokeTarget | Out-Null
                throw 'Start-SmokeTarget unexpectedly returned.'
            }
            catch {
                if ($_.Exception.Message -notlike 'Timed out waiting for smoke target main window:*') {
                    throw
                }
            }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }
            if (-not [System.IO.File]::Exists('{{EscapePowerShellPath(grandchildStartedPath)}}')) {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'FAILED: grandchild process was not observed before cleanup.')
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-transient-grandchild.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
            AssertNoProcessCommandLineContains(token);
        }
        finally
        {
            KillProcessesCommandLineContains(token);
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxArtifactPreflight_ShouldCleanChildWhenRootExitsBeforeFirstStartupPoll()
    {
        var tempRoot = CreateTempRoot();
        var token = $"short-root-child-{Guid.NewGuid():N}";
        try
        {
            var smokeProjectRoot = Path.Combine(tempRoot, token);
            var smokeTargetPath = CreateTransientGrandchildSmokeTarget(smokeProjectRoot, token, rootSleepSeconds: 0);
            var childStartedPath = Path.Combine(smokeProjectRoot, "child-launched.txt");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            try {
            $ErrorActionPreference = 'Stop'
            {{GetSmokeTargetFunctionBootstrap(scriptPath)}}

            $SmokeTargetPath = '{{EscapePowerShellPath(smokeTargetPath)}}'
            $SmokeTargetStartupTimeoutSeconds = 0
            try {
                Start-SmokeTarget | Out-Null
                throw 'Start-SmokeTarget unexpectedly returned.'
            }
            catch {
                if ($_.Exception.Message -notlike 'Timed out waiting for smoke target main window:*') {
                    throw
                }
            }
            if (-not [System.IO.File]::Exists('{{EscapePowerShellPath(childStartedPath)}}')) {
                throw 'short-lived smoke target did not launch its child before exiting.'
            }
            $remaining = @(Get-CimInstance Win32_Process | Where-Object {
                $_.ProcessId -ne $PID -and $_.CommandLine -like '*{{token}}*'
            })
            if ($remaining.Count -gt 0) {
                throw "Short-lived root cleanup left process IDs running: $($remaining.ProcessId -join ', ')"
            }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-short-root-child.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
            AssertNoProcessCommandLineContains(token);
        }
        finally
        {
            KillProcessesCommandLineContains(token);
            DeleteTempRootWithRetry(tempRoot);
        }
    }

    [Fact]
    public void SandboxArtifactPreflight_ShouldCleanStartupSnapshotsAfterMainWindowReady()
    {
        var tempRoot = CreateTempRoot();
        var token = $"startup-success-grandchild-{Guid.NewGuid():N}";
        try
        {
            var smokeProjectRoot = Path.Combine(tempRoot, token);
            var smokeTargetPath = CreateTransientGrandchildWindowTarget(smokeProjectRoot, token);
            var markerPath = Path.Combine(smokeProjectRoot, "grandchild-started.txt");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            try {
            $ErrorActionPreference = 'Stop'
            {{GetSmokeTargetFunctionBootstrap(scriptPath)}}

            $SmokeTargetPath = '{{EscapePowerShellPath(smokeTargetPath)}}'
            $SmokeTargetStartupTimeoutSeconds = 15
            $process = Start-SmokeTarget
            $rootProcessId = $process.Id
            try {
                if (-not [System.IO.File]::Exists('{{EscapePowerShellPath(markerPath)}}')) {
                    throw 'grandchild process was not observed before cleanup.'
                }
            }
            finally {
                Stop-SmokeTarget -Process $process
            }
            $remaining = @(Get-CimInstance Win32_Process | Where-Object {
                $_.ProcessId -ne $PID -and $_.CommandLine -like '*{{token}}*'
            })
            if ($remaining.Count -gt 0) {
                throw "Startup success cleanup left process IDs running: $($remaining.ProcessId -join ', ') Root: $rootProcessId"
            }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-startup-success-grandchild.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
            AssertNoProcessCommandLineContains(token);
        }
        finally
        {
            KillProcessesCommandLineContains(token);
            DeleteTempRootWithRetry(tempRoot);
        }
    }

    [Fact]
    public void SandboxArtifactPreflight_ShouldCleanStartupSnapshotsAfterRootExitsBeforeStop()
    {
        var tempRoot = CreateTempRoot();
        var token = $"root-exited-grandchild-{Guid.NewGuid():N}";
        try
        {
            var smokeProjectRoot = Path.Combine(tempRoot, token);
            var smokeTargetPath = CreateTransientGrandchildWindowTarget(smokeProjectRoot, token);
            var markerPath = Path.Combine(smokeProjectRoot, "grandchild-started.txt");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            try {
            $ErrorActionPreference = 'Stop'
            {{GetSmokeTargetFunctionBootstrap(scriptPath)}}

            $SmokeTargetPath = '{{EscapePowerShellPath(smokeTargetPath)}}'
            $SmokeTargetStartupTimeoutSeconds = 15
            $process = Start-SmokeTarget
            try {
                if (-not [System.IO.File]::Exists('{{EscapePowerShellPath(markerPath)}}')) {
                    throw 'grandchild process was not observed before cleanup.'
                }

                if (-not $process.HasExited) {
                    $process.Kill()
                    if (-not $process.WaitForExit(5000)) {
                        throw 'smoke target root did not exit before cleanup.'
                    }
                }

                Stop-SmokeTarget -Process $process
            }
            finally {
                $process.Dispose()
            }

            $remaining = @(Get-CimInstance Win32_Process | Where-Object {
                $_.ProcessId -ne $PID -and $_.CommandLine -like '*{{token}}*'
            })
            if ($remaining.Count -gt 0) {
                throw "Root-exited startup cleanup left process IDs running: $($remaining.ProcessId -join ', ')"
            }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-root-exited-grandchild.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
            AssertNoProcessCommandLineContains(token);
        }
        finally
        {
            KillProcessesCommandLineContains(token);
            DeleteTempRootWithRetry(tempRoot);
        }
    }

    private static string CreateChildSpawnerExecutable(string projectRoot, string token)
    {
        Directory.CreateDirectory(projectRoot);
        var projectPath = Path.Combine(projectRoot, "ChildSpawner.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        var tokenLiteral = System.Text.Json.JsonSerializer.Serialize(ToPowerShellSingleQuotedLiteral(token));
        var childStartedPathLiteral = System.Text.Json.JsonSerializer.Serialize(ToPowerShellSingleQuotedLiteral(Path.Combine(projectRoot, "child-started.txt")));
        File.WriteAllText(
            Path.Combine(projectRoot, "Program.cs"),
            $$"""
            var childCode = "$marker = " + {{tokenLiteral}} + "; [System.IO.File]::WriteAllText(" + {{childStartedPathLiteral}} + ", 'started'); Start-Sleep -Seconds 30";
            var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(childCode);
            System.Diagnostics.Process.Start(startInfo);
            Thread.Sleep(TimeSpan.FromSeconds(2));
            """);

        var publishRoot = Path.Combine(projectRoot, "publish");
        var result = RunProcess("dotnet", "publish", projectPath, "-c", "Release", "-o", publishRoot, "-v", "q");
        result.ExitCode.Should().Be(0, "PowerShell output: {0}", result.Output);
        return Path.Combine(publishRoot, "ChildSpawner.exe");
    }

    private static void DeleteTempRootWithRetry(string tempRoot)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                DeleteTempRoot(tempRoot);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static string CreateTransientGrandchildSmokeTarget(string projectRoot, string token, int rootSleepSeconds = 30)
    {
        Directory.CreateDirectory(projectRoot);
        var projectPath = Path.Combine(projectRoot, "TransientGrandchildTarget.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """);
        var launcherPath = Path.Combine(projectRoot, "launch-grandchild.ps1");
        var markerPath = Path.Combine(projectRoot, "grandchild-started.txt");
        var grandchildCode = "$marker = " + ToPowerShellSingleQuotedLiteral(token) + "; Start-Sleep -Seconds 30";
        File.WriteAllText(launcherPath, $$"""
            $grandchildCode = {{ToPowerShellSingleQuotedLiteral(grandchildCode)}}
            Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $grandchildCode) | Out-Null
            [System.IO.File]::WriteAllText({{ToPowerShellSingleQuotedLiteral(markerPath)}}, 'started')
            Start-Sleep -Seconds 1
            """);
        var launcherPathLiteral = System.Text.Json.JsonSerializer.Serialize(launcherPath);
        var childLaunchedPathLiteral = System.Text.Json.JsonSerializer.Serialize(Path.Combine(projectRoot, "child-launched.txt"));
        File.WriteAllText(Path.Combine(projectRoot, "Program.cs"), $$"""
            var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-NoProfile"); startInfo.ArgumentList.Add("-ExecutionPolicy"); startInfo.ArgumentList.Add("Bypass"); startInfo.ArgumentList.Add("-File"); startInfo.ArgumentList.Add({{launcherPathLiteral}});
            System.Diagnostics.Process.Start(startInfo);
            System.IO.File.WriteAllText({{childLaunchedPathLiteral}}, "started");
            Thread.Sleep(TimeSpan.FromSeconds({{rootSleepSeconds}}));
            """);
        var publishRoot = Path.Combine(projectRoot, "publish");
        var result = RunProcess("dotnet", "publish", projectPath, "-c", "Release", "-o", publishRoot, "-v", "q");
        result.ExitCode.Should().Be(0, "PowerShell output: {0}", result.Output);
        return Path.Combine(publishRoot, "TransientGrandchildTarget.exe");
    }

    private static string CreateTransientGrandchildWindowTarget(string projectRoot, string token)
    {
        Directory.CreateDirectory(projectRoot);
        var projectPath = Path.Combine(projectRoot, "TransientGrandchildWindowTarget.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """);
        var launcherPath = CreateGrandchildLauncher(projectRoot, token);
        var launcherPathLiteral = System.Text.Json.JsonSerializer.Serialize(launcherPath);
        File.WriteAllText(Path.Combine(projectRoot, "Program.cs"), $$"""
            internal static class Program
            {
                [System.STAThread]
                private static void Main()
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false };
                    System.Threading.Thread.Sleep(System.TimeSpan.FromMilliseconds(500));
                    startInfo.ArgumentList.Add("-NoProfile"); startInfo.ArgumentList.Add("-ExecutionPolicy"); startInfo.ArgumentList.Add("Bypass"); startInfo.ArgumentList.Add("-File"); startInfo.ArgumentList.Add({{launcherPathLiteral}});
                    System.Diagnostics.Process.Start(startInfo);
                    System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(2));
                    var app = new System.Windows.Application();
                    app.Run(new System.Windows.Window { Width = 240, Height = 120, Title = "Startup snapshot target" });
                }
            }
            """);
        var publishRoot = Path.Combine(projectRoot, "publish");
        var result = RunProcess("dotnet", "publish", projectPath, "-c", "Release", "-o", publishRoot, "-v", "q");
        result.ExitCode.Should().Be(0, "PowerShell output: {0}", result.Output);
        return Path.Combine(publishRoot, "TransientGrandchildWindowTarget.exe");
    }

    private static string CreateGrandchildLauncher(string projectRoot, string token)
    {
        var launcherPath = Path.Combine(projectRoot, "launch-grandchild.ps1");
        var markerPath = Path.Combine(projectRoot, "grandchild-started.txt");
        var grandchildCode = "$marker = " + ToPowerShellSingleQuotedLiteral(token) + "; Start-Sleep -Seconds 30";
        File.WriteAllText(launcherPath, $$"""
            $grandchildCode = {{ToPowerShellSingleQuotedLiteral(grandchildCode)}}
            Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $grandchildCode) | Out-Null
            [System.IO.File]::WriteAllText({{ToPowerShellSingleQuotedLiteral(markerPath)}}, 'started')
            Start-Sleep -Seconds 1
            """);
        return launcherPath;
    }
}
