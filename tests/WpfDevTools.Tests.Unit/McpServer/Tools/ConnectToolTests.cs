using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolTests
{
    [Fact]
    public async Task Execute_WithInvalidProcessId_ShouldReturnError()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { processId = 999999 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithNonWpfProcess_ShouldReturnError()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var tool = new ConnectTool(new SessionManager());

        // Find a system process that is definitely not WPF (e.g., svchost, System, Idle)
        var systemProcesses = System.Diagnostics.Process.GetProcessesByName("svchost");
        if (systemProcesses.Length == 0)
        {
            // Fallback to other system processes
            systemProcesses = System.Diagnostics.Process.GetProcessesByName("System");
        }

        if (systemProcesses.Length == 0)
        {
            return; // Skip if no suitable process found
        }

        var nonWpfProcessId = systemProcesses[0].Id;
        var parameters = new { processId = nonWpfProcessId };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();

        // Cleanup
        foreach (var proc in systemProcesses)
        {
            proc.Dispose();
        }
    }

    [Fact]
    public void Constructor_WithUnsignedDll_ShouldThrowWhenSignatureCheckEnabled()
    {
        // Arrange - ensure signature check is enabled (no skip scope)
        var unsignedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

        // Act & Assert - should throw because DLL is not signed (or signature invalid)
        var act = () => new ConnectTool(new SessionManager(), unsignedDllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signature*");
    }

    [Fact]
    public void Constructor_WithSkipSignatureCheck_ShouldNotThrow()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var unsignedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

        // Act & Assert - should not throw when signature check is skipped
        var act = () => new ConnectTool(new SessionManager(), unsignedDllPath);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("..\\..\\System32\\evil.dll")]
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("\\\\network\\share\\evil.dll")]
    public void Constructor_WithMaliciousPath_ShouldThrow(string maliciousPath)
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();

        // Act & Assert - should throw due to path validation
        var act = () => new ConnectTool(new SessionManager(), maliciousPath);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithPathOutsideAppDirectory_ShouldThrow()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var outsidePath = Path.Combine(Path.GetTempPath(), "evil.dll");

        // Act & Assert - should throw because path is outside application directory
        var act = () => new ConnectTool(new SessionManager(), outsidePath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*");
    }

    [Fact]
    public async Task Execute_RateLimitExceeded_ShouldReturnError()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var sessionManager = new SessionManager(maxRequestsPerMinute: 2);
        var tool = new ConnectTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Consume rate limit
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Act - third request should be rate limited
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Rate limit");
    }
}

