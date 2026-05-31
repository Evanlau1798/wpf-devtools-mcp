using FluentAssertions;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxArtifactPreflight_ShouldStopSmokeTargetWhenStartupTimesOut()
    {
        var tempRoot = CreateTempRoot();
        var token = $"smoke-startup-timeout-{Guid.NewGuid():N}";
        try
        {
            var smokeProjectRoot = Path.Combine(tempRoot, token);
            var smokeTargetPath = CreateSleeperExecutable(smokeProjectRoot);
            var childStartedPath = Path.Combine(smokeProjectRoot, "child-started.txt");
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
            if (-not [System.IO.File]::Exists('{{EscapePowerShellPath(childStartedPath)}}')) {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'FAILED: smoke target child process was not observed before cleanup.')
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-start-smoke-target.ps1");
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
    public void SandboxArtifactPreflight_ShouldSurfaceCleanupFailureWhenStartupTimesOut()
    {
        var tempRoot = CreateTempRoot();
        var token = $"smoke-cleanup-failure-{Guid.NewGuid():N}";
        try
        {
            var smokeTargetPath = CreateSleeperExecutable(Path.Combine(tempRoot, token));
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{GetSmokeTargetFunctionBootstrap(scriptPath)}}
                function Stop-ProcessSnapshots { throw 'simulated cleanup failure' }
                $SmokeTargetPath = '{{EscapePowerShellPath(smokeTargetPath)}}'
                $SmokeTargetStartupTimeoutSeconds = 3
                Start-SmokeTarget | Out-Null
                throw 'Start-SmokeTarget unexpectedly returned.'
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', $_.Exception.Message)
                if ($_.Exception.Message -notlike '*Timed out waiting for smoke target main window:*') { exit 1 }
                if ($_.Exception.Message -notlike '*simulated cleanup failure*') { exit 1 }
            }
            """;
            var probePath = Path.Combine(tempRoot, "probe-cleanup-failure.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
        }
        finally
        {
            KillProcessesCommandLineContains(token);
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxArtifactPreflight_ShouldPreservePrimaryFailureWhenFinalCleanupFails()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1"));

        runner.Should().Contain("$preflightFailureMessage");
        runner.Should().Contain("Preflight cleanup failed after primary failure");
        runner.Should().Contain("Write-PreflightSummary -Status 'FAIL' -Message $combinedMessage");
        runner.Should().Contain("Write-PreflightResult -Value \"FAIL $RunId $timestamp ArtifactPreflight $combinedMessage\"");
    }

    [Fact]
    public void SandboxArtifactPreflight_StopSmokeTarget_ShouldUseIdentitySnapshotsForDescendantCleanup()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ProcessCleanup.ps1"));
        var stopBlock = runner.Substring(
            runner.IndexOf("function Stop-SmokeTarget", StringComparison.Ordinal),
            runner.Length -
            runner.IndexOf("function Stop-SmokeTarget", StringComparison.Ordinal));

        runner.Should().Contain("New-ProcessSnapshot");
        runner.Should().Contain("Get-DescendantProcessSnapshots");
        runner.Should().Contain("Test-ProcessSnapshotExists");
        runner.Should().Contain("CreationDateUtcTicks");
        runner.Should().Contain("DescendantCutoffUtcTicks");
        runner.Should().Contain(".ToUniversalTime().Ticks");
        runner.Should().NotContain("Stop-ProcessTree -ProcessId $Process.Id");
        runner.Should().NotContain("CreationDate = [string]$child.CreationDate");
        runner.Should().Contain("Merge-ProcessSnapshots");
        runner.Should().Contain("Stop-ExistingProcessSnapshots -Snapshots $Snapshots");
        runner.Should().Contain("[object[]]$ScanRoots = @()");
        runner.Should().Contain("CreationCutoffUtcTicks");
        runner.Should().Contain("Stop-ExistingProcessSnapshots -Snapshots $Snapshots");
        runner.Should().Contain("Expand-ProcessSnapshots");
        stopBlock.Should().Contain("while (-not $Process.HasExited");
        stopBlock.Should().Contain("$descendantSnapshots += @(Get-DescendantProcessSnapshots -ParentProcessId $processId -CreationStartUtcTicks (Get-ProcessSnapshotStartCutoff -Snapshot $rootSnapshot))");
        stopBlock.Should().Contain("if (-not $Process.WaitForExit(5000))");
        stopBlock.Should().Contain("Smoke target did not exit after force kill");
        stopBlock.Should().Contain("Stop-ProcessSnapshots -Snapshots @(@($rootSnapshot) + @($descendantSnapshots)) -ScanRoots @($rootSnapshot)");
    }

    [Fact]
    public void TestProcessRunner_ShouldKillTimedOutProcessTreeBeforeThrowing()
    {
        var token = $"test-helper-timeout-{Guid.NewGuid():N}";
        try
        {
            var exception = Assert.Throws<TimeoutException>(() => RunProcess(
                "powershell.exe",
                TimeSpan.FromSeconds(1),
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                $$"""
                $childCode = '$childMarker = "{{token}}"; Start-Sleep -Seconds 30'
                Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $childCode) -WindowStyle Hidden | Out-Null
                do {
                    Start-Sleep -Milliseconds 100
                    $childMatches = @(Get-CimInstance Win32_Process | Where-Object {
                        $_.ProcessId -ne $PID -and $_.CommandLine -like "*{{token}}*"
                    })
                } while ($childMatches.Count -lt 1)
                Start-Sleep -Seconds 30
                """));

            exception.Message.Should().Contain("timed out");
            AssertNoProcessCommandLineContains(token);
        }
        finally
        {
            KillProcessesCommandLineContains(token);
        }
    }

    [Fact]
    public void TestProcessRunner_ProcessTreeSnapshotShouldIncludeLiveChildProcess()
    {
        var tempRoot = CreateTempRoot();
        var token = $"snapshot-child-{Guid.NewGuid():N}";
        Process? parent = null;
        try
        {
            var childIdPath = Path.Combine(tempRoot, "child-id.txt");
            parent = StartPowerShellWithoutRedirect("-Command", $$"""
            $child = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-Command', '$marker = "{{token}}"; Start-Sleep -Seconds 30') -WindowStyle Hidden -PassThru
            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(childIdPath)}}', [string]$child.Id)
            Start-Sleep -Seconds 30
            """);
            parent.Start().Should().BeTrue();

            var childId = WaitForChildId(childIdPath);
            GetProcessTreeIds(parent.Id).Should().Contain(childId);
        }
        finally
        {
            if (parent is { HasExited: false })
            {
                parent.Kill(entireProcessTree: true);
                parent.WaitForExit(30000).Should().BeTrue();
            }

            parent?.Dispose();
            KillProcessesCommandLineContains(token);
            DeleteTempRoot(tempRoot);
        }
    }

    private static string CreateSleeperExecutable(string projectRoot)
    {
        Directory.CreateDirectory(projectRoot);
        var projectPath = Path.Combine(projectRoot, "SmokeTarget.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        var markerLiteral = System.Text.Json.JsonSerializer.Serialize(ToPowerShellSingleQuotedLiteral(projectRoot));
        var childStartedPathLiteral = System.Text.Json.JsonSerializer.Serialize(Path.Combine(projectRoot, "child-started.txt"));
        File.WriteAllText(
            Path.Combine(projectRoot, "Program.cs"),
            $$"""
            var childCode = "$childMarker = " + {{markerLiteral}} + "; Start-Sleep -Seconds 30";
            var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(childCode);
            System.Diagnostics.Process.Start(startInfo);
            System.IO.File.WriteAllText({{childStartedPathLiteral}}, "started");
            Thread.Sleep(TimeSpan.FromSeconds(30));
            """);

        var publishRoot = Path.Combine(projectRoot, "publish");
        var result = RunProcess("dotnet", "publish", projectPath, "-c", "Release", "-o", publishRoot, "-v", "q");
        result.ExitCode.Should().Be(0, "PowerShell output: {0}", result.Output);
        return Path.Combine(publishRoot, "SmokeTarget.exe");
    }

    private static CommandResult RunPowerShellFileWithoutRedirect(string scriptPath)
    {
        using var process = StartPowerShellWithoutRedirect("-File", scriptPath);
        process.Start().Should().BeTrue();
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(30000).Should().BeTrue();
            throw new TimeoutException($"PowerShell verification timed out: {scriptPath}");
        }

        return new CommandResult(process.ExitCode, "");
    }

    private static Process StartPowerShellWithoutRedirect(params string[] arguments)
    {
        var process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass" }.Concat(arguments))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.WorkingDirectory = RepoRoot;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        return process;
    }

    private static int WaitForChildId(string childIdPath)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(childIdPath) && int.TryParse(File.ReadAllText(childIdPath), out var childId))
            {
                return childId;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("Timed out waiting for child process id.");
    }

    private static string GetSmokeTargetFunctionBootstrap(string scriptPath)
        => $$"""
        $script = [System.IO.File]::ReadAllText([System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName('{{EscapePowerShellPath(scriptPath)}}'), 'SandboxCi.ProcessCleanup.ps1'))
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($script, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) {
            throw "PowerShell parser error: $($errors[0].Message)"
        }

        $functionNames = @(
            'Get-SmokeTargetProcessId',
            'Set-SmokeTargetRootSnapshot',
            'Get-SmokeTargetRootSnapshot',
            'Remove-SmokeTargetRootSnapshot',
            'New-ProcessSnapshot',
            'New-ProcessSnapshotFromProcess',
            'Get-ProcessSnapshotKey',
            'Set-SmokeTargetKnownSnapshots',
            'Get-SmokeTargetKnownSnapshots',
            'Remove-SmokeTargetKnownSnapshots',
            'Get-DescendantProcessSnapshots',
            'Test-ProcessSnapshotExists',
            'Get-MatchingProcessFromSnapshot',
            'Merge-ProcessSnapshots',
            'Get-ScanRootCutoff',
            'Get-ProcessSnapshotStartCutoff',
            'Update-ProcessSnapshotCutoffIfAlive',
            'Update-ProcessSnapshotCutoffFromProcess',
            'Expand-ProcessSnapshots',
            'Stop-ExistingProcessSnapshots',
            'Stop-ProcessSnapshots',
            'Stop-SmokeTarget',
            'Start-SmokeTarget'
        )
        foreach ($name in $functionNames) {
            $function = $ast.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
            }, $true)
            if ($null -eq $function) {
                throw "Missing function $name."
            }

            Invoke-Expression $function.Extent.Text
        }
        """;

    private static string ToPowerShellSingleQuotedLiteral(string value)
        => $"'{value.Replace("'", "''")}'";

    private static int[] GetProcessTreeIds(int rootProcessId)
    {
        var entries = GetProcessEntries();
        var childrenByParent = entries
            .GroupBy(entry => entry.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.ProcessId).ToArray());

        var processIds = new List<int>();
        AddDescendantProcessIds(rootProcessId, childrenByParent, processIds);
        processIds.Add(rootProcessId);
        return processIds.Distinct().ToArray();
    }

    private static void AddDescendantProcessIds(
        int processId,
        IReadOnlyDictionary<int, int[]> childrenByParent,
        List<int> processIds)
    {
        if (!childrenByParent.TryGetValue(processId, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            AddDescendantProcessIds(child, childrenByParent, processIds);
            processIds.Add(child);
        }
    }

    private static void WaitForProcessesToExit(IReadOnlyCollection<int> processIds, TimeSpan timeout, string fileName)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        int[] remaining;
        do
        {
            remaining = processIds.Where(ProcessExists).ToArray();
            if (remaining.Length == 0)
            {
                return;
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"{fileName} cleanup left process IDs running: {string.Join(", ", remaining)}");
    }

    private static bool ProcessExists(int processId)
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
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static List<ProcessEntry> GetProcessEntries()
    {
        using var snapshot = new SafeToolhelpSnapshotHandle(CreateToolhelp32Snapshot(2, 0));
        if (snapshot.IsInvalid)
        {
            throw new InvalidOperationException("CreateToolhelp32Snapshot failed.");
        }

        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        var entries = new List<ProcessEntry>();
        if (!Process32First(snapshot, ref entry))
        {
            throw new InvalidOperationException("Process32FirstW failed.");
        }

        do
        {
            entries.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId));
        }
        while (Process32Next(snapshot, ref entry));

        return entries;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    private static extern bool Process32First(SafeToolhelpSnapshotHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    private static extern bool Process32Next(SafeToolhelpSnapshotHandle snapshot, ref ProcessEntry32 entry);

    private readonly record struct ProcessEntry(int ProcessId, int ParentProcessId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    private sealed class SafeToolhelpSnapshotHandle(IntPtr handle) : SafeHandle(handle, ownsHandle: true)
    {
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle()
            => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static void KillProcessesCommandLineContains(string token)
    {
        var result = RunPowerShell($$"""
        $matches = @(Get-CimInstance Win32_Process | Where-Object {
            $_.ProcessId -ne $PID -and $_.CommandLine -like '*{{token}}*'
        })
        foreach ($match in $matches) {
            Stop-Process -Id $match.ProcessId -Force -ErrorAction SilentlyContinue
        }
        """);

        result.ExitCode.Should().Be(0, result.Output);
    }
}
