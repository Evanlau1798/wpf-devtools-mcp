using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public class InspectorHostLoggingTests : IDisposable
{
    private readonly string _tempLogDir;
    private readonly IDisposable _plaintextPolicy;

    public InspectorHostLoggingTests()
    {
        _plaintextPolicy = UnsafePlaintextInspectorHostTestEnvironment.BeginScope();
        _tempLogDir = Path.Combine(Path.GetTempPath(), $"WpfDevTools_LogTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempLogDir);
    }

    public void Dispose()
    {
        _plaintextPolicy.Dispose();
        try
        {
            if (Directory.Exists(_tempLogDir))
                Directory.Delete(_tempLogDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void InspectorHost_LogError_ShouldNotBlockCaller()
    {
        // Arrange - create host and measure time for high-frequency logging
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        var sw = Stopwatch.StartNew();

        // Act - trigger multiple errors that go through LogError internally
        // Since LogError is private, we test indirectly through the host behavior
        // by starting and stopping rapidly (which triggers logging)
        host.Stop();

        sw.Stop();

        // Assert - stop should complete quickly (< 5 seconds)
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            "InspectorHost.Stop() should not be blocked by synchronous logging");
    }

    [Fact]
    public async Task FileLogger_InSharedNamespace_ShouldBeAccessible()
    {
        // Arrange - verify FileLogger is available in Shared.Utilities namespace
        var logPath = Path.Combine(_tempLogDir, "test.log");
        using var logger = new FileLogger(logPath);

        // Act
        logger.LogError("test message from Inspector context");

        // Wait for async queue to flush
        await logger.DisposeAsync();

        // Assert
        File.Exists(logPath).Should().BeTrue();
        var content = File.ReadAllText(logPath);
        content.Should().Contain("[ERROR]");
        content.Should().Contain("test message from Inspector context");
    }

    [Fact]
    public async Task FileLogger_HighFrequencyLogging_ShouldNotBlock()
    {
        // Arrange
        var logPath = Path.Combine(_tempLogDir, "perf.log");
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistedEntries = new List<string>();
        using var logger = new FileLogger(
            logPath,
            TimeSpan.FromSeconds(5),
            async (entries, cancellationToken) =>
            {
                writeStarted.TrySetResult();
                await releaseWrite.Task.WaitAsync(cancellationToken);
                lock (persistedEntries)
                {
                    persistedEntries.AddRange(entries);
                }
            });

        logger.LogError("Blocked writer primer");
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - log 1000 messages while the background writer is blocked.
        var loggingTask = Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                logger.LogError($"High frequency message {i}");
            }
        });

        // Assert - foreground logging should enqueue without waiting for the writer to unblock.
        await loggingTask.WaitAsync(TimeSpan.FromSeconds(1));
        releaseWrite.SetResult();
        await logger.DisposeAsync();
        var content = string.Concat(persistedEntries);
        content.Should().Contain("High frequency message 0");
        content.Should().Contain("High frequency message 999");
    }

    [Fact]
    public async Task FileLogger_Dispose_ShouldFlushRemainingLogs()
    {
        // Arrange
        var logPath = Path.Combine(_tempLogDir, "flush.log");
        var logger = new FileLogger(logPath);

        // Act - write messages and dispose immediately
        logger.LogError("before dispose");
        logger.Dispose();

        // Small delay for file system
        await Task.Delay(100);

        // Assert - message should be flushed on dispose
        File.Exists(logPath).Should().BeTrue();
        var content = File.ReadAllText(logPath);
        content.Should().Contain("before dispose");
    }
}
