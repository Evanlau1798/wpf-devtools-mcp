using FluentAssertions;
using System.Diagnostics;
using System.IO;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

public sealed class LiveTestProcessCleanupBehaviorTests
{
    [Fact]
    public void StopAndDispose_ShouldKillRunningProcessTree()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        var childIdPath = Path.Combine(tempRoot, "child-id.txt");
        var token = $"live-cleanup-child-{Guid.NewGuid():N}";
        Process? parent = null;
        var childId = 0;

        try
        {
            parent = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -Command " +
                    QuotePowerShellCommand($"""
                    $child = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', '$marker = "{token}"; Start-Sleep -Seconds 30') -WindowStyle Hidden -PassThru
                    [System.IO.File]::WriteAllText('{EscapePowerShellPath(childIdPath)}', [string]$child.Id)
                    Start-Sleep -Seconds 30
                    """),
                CreateNoWindow = true,
                UseShellExecute = false
            });

            parent.Should().NotBeNull();
            childId = WaitForChildId(childIdPath);
            TryGetProcess(childId, out _).Should().BeTrue();

            LiveTestProcessCleanup.StopAndDispose(parent, timeoutMilliseconds: 5000);
            parent = null;

            TryGetProcess(childId, out _).Should().BeFalse(
                "live TestApp cleanup must stop child processes before the next integration fixture starts");
        }
        finally
        {
            if (parent is { HasExited: false })
            {
                parent.Kill(entireProcessTree: true);
                parent.WaitForExit(30000).Should().BeTrue();
            }

            parent?.Dispose();
            KillProcessIfAlive(childId);
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
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

    private static bool TryGetProcess(int processId, out Process? process)
    {
        process = null;
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void KillProcessIfAlive(int processId)
    {
        if (TryGetProcess(processId, out var process))
        {
            using (process)
            {
                process!.Kill(entireProcessTree: true);
                process.WaitForExit(30000);
            }
        }
    }

    private static string QuotePowerShellCommand(string command)
        => "\"" + command.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string EscapePowerShellPath(string path)
        => path.Replace("'", "''", StringComparison.Ordinal);
}
