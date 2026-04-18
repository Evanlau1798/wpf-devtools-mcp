using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Tests.Unit.McpServer;

public class FileLoggerTests : IAsyncDisposable
{
    private readonly string _testLogPath;
    private readonly FileLogger _logger;

    public FileLoggerTests()
    {
        _testLogPath = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid()}.log");
        _logger = new FileLogger(_testLogPath);
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose logger to flush queue
        await _logger.DisposeAsync();

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
}
