using FluentAssertions;
using Microsoft.Extensions.Logging;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for FileLoggerProvider - the ILoggerProvider adapter for FileLogger.
/// </summary>
public class FileLoggerProviderTests : IDisposable
{
    private readonly string _logFilePath;
    private readonly FileLogger _fileLogger;

    public FileLoggerProviderTests()
    {
        _logFilePath = Path.Combine(Path.GetTempPath(), $"test_logger_provider_{Guid.NewGuid():N}.log");
        _fileLogger = new FileLogger(_logFilePath);
    }

    public void Dispose()
    {
        _fileLogger.Dispose();
        try { File.Delete(_logFilePath); } catch { /* cleanup */ }
    }

    [Fact]
    public void Constructor_WithNullFileLogger_ShouldThrowArgumentNullException()
    {
        var act = () => new FileLoggerProvider(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileLogger");
    }

    [Fact]
    public void CreateLogger_ShouldReturnNonNullLogger()
    {
        using var provider = new FileLoggerProvider(_fileLogger);

        var logger = provider.CreateLogger("TestCategory");

        logger.Should().NotBeNull();
    }

    [Fact]
    public void CreateLogger_WithDifferentCategories_ShouldReturnDistinctLoggers()
    {
        using var provider = new FileLoggerProvider(_fileLogger);

        var logger1 = provider.CreateLogger("Category1");
        var logger2 = provider.CreateLogger("Category2");

        logger1.Should().NotBeSameAs(logger2);
    }

    [Fact]
    public void Logger_IsEnabled_ShouldReturnTrueForInformationAndAbove()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("Test");

        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
        logger.IsEnabled(LogLevel.Critical).Should().BeTrue();
    }

    [Fact]
    public void Logger_IsEnabled_ShouldReturnFalseForDebugAndTrace()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("Test");

        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        logger.IsEnabled(LogLevel.Trace).Should().BeFalse();
    }

    [Fact]
    public void Logger_BeginScope_ShouldReturnNull()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("Test");

        var scope = logger.BeginScope("test scope");

        scope.Should().BeNull();
    }

    [Fact]
    public void Logger_LogInformation_ShouldNotThrow()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("TestCategory");

        var act = () => logger.LogInformation("Test message {Value}", 42);

        act.Should().NotThrow();
    }

    [Fact]
    public void Logger_LogError_ShouldNotThrow()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("TestCategory");

        var act = () => logger.LogError(new InvalidOperationException("test"), "Error occurred");

        act.Should().NotThrow();
    }

    [Fact]
    public void Logger_LogWarning_ShouldNotThrow()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("TestCategory");

        var act = () => logger.LogWarning("Warning message");

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var provider = new FileLoggerProvider(_fileLogger);

        var act = () => provider.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        var provider = new FileLoggerProvider(_fileLogger);

        var act = () =>
        {
            provider.Dispose();
            provider.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Logger_LogInformation_ShouldWriteToFile()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("ContentTest");

        logger.LogInformation("Hello from test {Id}", 42);

        // FileLogger uses Channel-based async I/O; give it time to flush
        await Task.Delay(200);
        _fileLogger.Dispose();

        var content = await File.ReadAllTextAsync(_logFilePath);
        content.Should().Contain("[ContentTest]");
        content.Should().Contain("Hello from test 42");
    }

    [Fact]
    public async Task Logger_LogError_ShouldWriteExceptionToFile()
    {
        using var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("ErrorTest");

        logger.LogError(new InvalidOperationException("test-exception"), "Something failed");

        await Task.Delay(200);
        _fileLogger.Dispose();

        var content = await File.ReadAllTextAsync(_logFilePath);
        content.Should().Contain("[ErrorTest]");
        content.Should().Contain("Something failed");
        content.Should().Contain("test-exception");
    }

    [Fact]
    public async Task Logger_AfterDispose_ShouldNotWriteToFile()
    {
        var provider = new FileLoggerProvider(_fileLogger);
        var logger = provider.CreateLogger("DisposedTest");

        provider.Dispose();
        logger.LogInformation("Should not appear");

        await Task.Delay(200);
        _fileLogger.Dispose();

        var content = File.Exists(_logFilePath) ? await File.ReadAllTextAsync(_logFilePath) : "";
        content.Should().NotContain("Should not appear");
    }
}
