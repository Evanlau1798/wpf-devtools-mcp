using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Utilities;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.McpServer;

public class FileLoggerPerformanceTests : IDisposable
{
    private readonly string _testLogPath;
    private readonly FileLogger _logger;

    public FileLoggerPerformanceTests()
    {
        _testLogPath = Path.Combine(Path.GetTempPath(), $"test_perf_log_{Guid.NewGuid()}.log");
        _logger = new FileLogger(_testLogPath);
    }

    public void Dispose()
    {
        // Cleanup
        try
        {
            if (File.Exists(_testLogPath))
                File.Delete(_testLogPath);
            if (File.Exists(_testLogPath + ".old"))
                File.Delete(_testLogPath + ".old");
        }
        catch
        {
            // Ignore
        }
    }

    [Fact]
    public async Task LogAsync_ShouldNotBlockCallingThread()
    {
        // Arrange - measure time for 1000 log calls
        var sw = Stopwatch.StartNew();

        // Act - log 1000 messages (should be fast if async)
        var tasks = Enumerable.Range(0, 1000).Select(i =>
            Task.Run(() => _logger.LogInfo($"Message {i}"))
        ).ToArray();

        await Task.WhenAll(tasks);
        sw.Stop();

        // Assert - should complete in < 500ms if truly async
        // (synchronous I/O would take much longer)
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "async logging should not block calling threads");
    }

    [Fact]
    public async Task LogAsync_HighThroughput_ShouldNotDropMessages()
    {
        // Arrange
        const int messageCount = 1000;

        // Act - rapid fire logging
        var tasks = Enumerable.Range(0, messageCount).Select(i =>
            Task.Run(() => _logger.LogInfo($"Message {i}"))
        ).ToArray();

        await Task.WhenAll(tasks);

        // Wait for background queue to flush
        await Task.Delay(1000);

        // Assert - all messages should be written
        var content = File.ReadAllText(_testLogPath);
        for (int i = 0; i < messageCount; i++)
        {
            content.Should().Contain($"Message {i}");
        }
    }
}
