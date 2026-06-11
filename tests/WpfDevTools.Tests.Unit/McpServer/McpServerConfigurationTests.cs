using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ProcessEnvironment")]
public sealed class McpServerConfigurationTests
{
    private const string RateLimitEnvVar = "WPFDEVTOOLS_RATE_LIMIT_RPM";

    [Fact]
    public void GetConfiguredRateLimitRequestsPerMinute_WithoutOverride_ShouldReturnDefault()
    {
        using var _ = new EnvironmentVariableScope(RateLimitEnvVar, null);

        McpServerConfiguration.GetConfiguredRateLimitRequestsPerMinute()
            .Should()
            .Be(McpServerConfiguration.RateLimitRequestsPerMinute);
    }

    [Fact]
    public void GetConfiguredRateLimitRequestsPerMinute_WithPositiveOverride_ShouldReturnOverride()
    {
        using var _ = new EnvironmentVariableScope(RateLimitEnvVar, "1200");

        McpServerConfiguration.GetConfiguredRateLimitRequestsPerMinute()
            .Should()
            .Be(1200);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void GetConfiguredRateLimitRequestsPerMinute_WithInvalidOverride_ShouldReturnDefault(string value)
    {
        using var _ = new EnvironmentVariableScope(RateLimitEnvVar, value);

        McpServerConfiguration.GetConfiguredRateLimitRequestsPerMinute()
            .Should()
            .Be(McpServerConfiguration.RateLimitRequestsPerMinute);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
