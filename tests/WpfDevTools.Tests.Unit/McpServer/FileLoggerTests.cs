using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Utilities;
using System.Diagnostics;
using System.Threading.Channels;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public class FileLoggerTests : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan BackgroundWriterStartTimeout = TimeSpan.FromSeconds(10);

    private readonly string _testLogPath;
    private readonly FileLogger _logger;

    public FileLoggerTests()
    {
        _testLogPath = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid()}.log");
        _logger = new FileLogger(_testLogPath);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose logger to flush queue
        await _logger.DisposeAsync().ConfigureAwait(false);

        // Cleanup test log files
        try
        {
            if (File.Exists(_testLogPath))
                File.Delete(_testLogPath);
            if (File.Exists(_testLogPath + ".old"))
                File.Delete(_testLogPath + ".old");
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private async Task<string> FlushAndReadLogAsync()
    {
        await _logger.DisposeAsync();
        File.Exists(_testLogPath).Should().BeTrue();
        return File.ReadAllText(_testLogPath);
    }

    [Fact]
    public async Task LogInfo_ShouldWriteToFile()
    {
        // Act
        _logger.LogInfo("Test info message");

        // Assert
        var content = await FlushAndReadLogAsync();
        content.Should().Contain("[INFO]");
        content.Should().Contain("Test info message");
    }

    [Fact]
    public async Task LogError_ShouldWriteToFile()
    {
        // Act
        _logger.LogError("Test error message");

        // Assert
        var content = await FlushAndReadLogAsync();
        content.Should().Contain("[ERROR]");
        content.Should().Contain("Test error message");
    }

    [Fact]
    public async Task LogDebug_ShouldWriteToFile()
    {
        // Arrange - lower minimum level to capture debug
        _logger.MinimumLevel = FileLogLevel.Debug;

        // Act
        _logger.LogDebug("Test debug message");

        // Assert
        var content = await FlushAndReadLogAsync();
        content.Should().Contain("[DEBUG]");
        content.Should().Contain("Test debug message");
    }

    [Fact]
    public async Task Log_ShouldIncludeTimestamp()
    {
        // Act
        _logger.LogInfo("Test message");

        // Assert
        var content = await FlushAndReadLogAsync();
        // Timestamp format: yyyy-MM-dd HH:mm:ss.fff
        content.Should().MatchRegex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}");
    }

    [Fact]
    public async Task Log_WithMultipleMessages_ShouldAppendToFile()
    {
        // Act
        _logger.LogInfo("Message 1");
        _logger.LogInfo("Message 2");
        _logger.LogInfo("Message 3");

        // Assert
        var content = await FlushAndReadLogAsync();
        content.Should().Contain("Message 1");
        content.Should().Contain("Message 2");
        content.Should().Contain("Message 3");
    }

    [Fact]
    public async Task Log_ConcurrentWrites_ShouldNotCorruptFile()
    {
        // Act - simulate concurrent writes
        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => _logger.LogInfo($"Concurrent message {i}"))
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var content = await FlushAndReadLogAsync();
        for (int i = 0; i < 10; i++)
        {
            content.Should().Contain($"Concurrent message {i}");
        }
    }

    [Fact]
    public async Task LogRotation_WhenFileSizeExceeds10MB_ShouldRotate()
    {
        // Arrange - write large messages to exceed 10MB
        var largeMessage = new string('X', 1024 * 1024); // 1MB message

        // Act - write 11 messages to exceed 10MB
        for (int i = 0; i < 11; i++)
        {
            _logger.LogInfo(largeMessage);
        }

        await _logger.DisposeAsync();

        // Assert - old file should exist
        File.Exists(_testLogPath + ".old").Should().BeTrue();
        File.Exists(_testLogPath).Should().BeTrue();

        // New file should be smaller than 10MB
        var newFileInfo = new FileInfo(_testLogPath);
        newFileInfo.Length.Should().BeLessThan(10 * 1024 * 1024);
    }

    [Fact]
    public async Task LogRotation_WhenOldFileExists_ShouldDeleteOldFile()
    {
        // Arrange - create an existing .old file
        File.WriteAllText(_testLogPath + ".old", "Old content");
        var largeMessage = new string('X', 1024 * 1024); // 1MB message

        // Act - write enough to trigger rotation
        for (int i = 0; i < 11; i++)
        {
            _logger.LogInfo(largeMessage);
        }

        await _logger.DisposeAsync();

        // Assert - old file should be replaced
        File.Exists(_testLogPath + ".old").Should().BeTrue();
        var oldContent = File.ReadAllText(_testLogPath + ".old");
        oldContent.Should().NotContain("Old content");
    }

    [Fact]
    public void LogFilePath_ShouldReturnCorrectPath()
    {
        // Assert
        _logger.LogFilePath.Should().Be(_testLogPath);
    }

    [Fact]
    public async Task DisposeAsync_WhenBackgroundWriterOutlivesShutdownTimeout_ShouldRecordTimeoutAndReturnWithinBoundedWindow()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownTimeout = TimeSpan.FromMilliseconds(200);
        var logger = new FileLogger(
            _testLogPath,
            shutdownTimeout: shutdownTimeout,
            writeEntriesOverride: async (_, _) =>
            {
                writeStarted.SetResult();
                await releaseWrite.Task;
            });

        logger.LogInfo("blocked write");
        await writeStarted.Task.WaitAsync(BackgroundWriterStartTimeout);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            await logger.DisposeAsync();
            stopwatch.Stop();

            logger.LastShutdownErrorForTesting.Should().BeOfType<TimeoutException>();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
                "logger shutdown should stay bounded even when hosted runners delay timers; single-budget arithmetic is verified separately");
        }
        finally
        {
            releaseWrite.TrySetResult();
            await logger.ProcessingTaskForTesting.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task DisposeAsync_WhenBackgroundWriterIsBlocked_ShouldReturnBeforeWaitingForShutdownBudget()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownTimeout = TimeSpan.FromMilliseconds(500);
        var logger = new FileLogger(
            _testLogPath,
            shutdownTimeout: shutdownTimeout,
            writeEntriesOverride: async (_, _) =>
            {
                writeStarted.SetResult();
                await releaseWrite.Task;
            });

        logger.LogInfo("blocked write");
        await writeStarted.Task.WaitAsync(BackgroundWriterStartTimeout);

        Task disposeTask;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            disposeTask = logger.DisposeAsync().AsTask();
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
                "DisposeAsync should yield to an asynchronous timeout wait instead of blocking the caller thread");
            disposeTask.IsCompleted.Should().BeFalse(
                "shutdown should still be waiting asynchronously while the writer is blocked");
        }
        finally
        {
            releaseWrite.TrySetResult();
        }

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        await logger.ProcessingTaskForTesting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposeAsync_WhenCalledOnBlockedSynchronizationContext_ShouldStillArmShutdownTimeout()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownTimeout = TimeSpan.FromMilliseconds(100);
        var logger = new FileLogger(
            _testLogPath,
            shutdownTimeout: shutdownTimeout,
            writeEntriesOverride: async (_, _) =>
            {
                writeStarted.SetResult();
                await releaseWrite.Task;
            });

        logger.LogInfo("blocked write");
        await writeStarted.Task.WaitAsync(BackgroundWriterStartTimeout);

        var previousContext = SynchronizationContext.Current;
        var blockedContext = new QueuedSynchronizationContext();
        Task disposeTask;
        try
        {
            SynchronizationContext.SetSynchronizationContext(blockedContext);
            disposeTask = logger.DisposeAsync().AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        try
        {
            var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2)));

            completed.Should().BeSameAs(disposeTask,
                "async shutdown timeout must not depend on the caller SynchronizationContext being pumped");
            logger.LastShutdownErrorForTesting.Should().BeOfType<TimeoutException>();
        }
        finally
        {
            releaseWrite.TrySetResult();
            blockedContext.Drain();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            await logger.ProcessingTaskForTesting.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void GetRemainingShutdownTimeout_ShouldReduceTheSingleSharedBudget()
    {
        var remaining = FileLogger.GetRemainingShutdownTimeout(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(75));

        remaining.Should().Be(TimeSpan.FromMilliseconds(125));
    }

    [Fact]
    public void GetRemainingShutdownTimeout_WhenElapsedExceedsBudget_ShouldClampToZero()
    {
        var remaining = FileLogger.GetRemainingShutdownTimeout(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(250));

        remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task ShouldContinueNet48ConsumerLoop_WhenChannelIsCompleted_ShouldReturnFalse()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryComplete();

        var canRead = await channel.Reader.WaitToReadAsync(CancellationToken.None);

        FileLogger.ShouldContinueNet48ConsumerLoop(canRead, CancellationToken.None).Should().BeFalse(
            "the NET48 consumer loop must stop once the channel is completed and drained so dispose does not wait until cancellation");
    }

    [Fact]
    public async Task ShouldContinueNet48ConsumerLoop_WhenEntriesRemain_ShouldReturnTrue()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("entry");

        var canRead = await channel.Reader.WaitToReadAsync(CancellationToken.None);

        FileLogger.ShouldContinueNet48ConsumerLoop(canRead, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldContinueNet48ConsumerLoop_WhenCancellationIsRequested_ShouldReturnFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        FileLogger.ShouldContinueNet48ConsumerLoop(canRead: true, cts.Token).Should().BeFalse();
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_callbacks)
            {
                _callbacks.Enqueue((d, state));
            }
        }

        public void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_callbacks)
                {
                    if (_callbacks.Count == 0)
                    {
                        return;
                    }

                    item = _callbacks.Dequeue();
                }

                item.Callback(item.State);
            }
        }
    }
}
