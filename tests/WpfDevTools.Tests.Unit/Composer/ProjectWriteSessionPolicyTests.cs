using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ProjectWriteSessionPolicyTests
{
    [Fact]
    public void AuthorizeSession_WhenPolicyIsUnsetAndExactGrantExists_ShouldAllow()
    {
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, null);
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, null);
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpf-project");

        var result = ProjectWritePolicy.AuthorizeSession(
            projectRoot,
            normalized => string.Equals(normalized, projectRoot, StringComparison.OrdinalIgnoreCase));

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeSession_WhenPolicyIsExplicitlyDisabled_ShouldIgnoreGrant()
    {
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "false");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, null);

        var result = ProjectWritePolicy.AuthorizeSession(
            Path.Combine(Path.GetTempPath(), "wpf-project"),
            _ => true);

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be("ProjectWritesDisabled");
    }

    [Fact]
    public void AuthorizeSession_WhenConfiguredRootCeilingExcludesGrant_ShouldDeny()
    {
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, null);
        using var roots = new EnvironmentVariableScope(
            McpServerConfiguration.AllowedProjectRootsEnvVar,
            Path.Combine(Path.GetTempPath(), "allowed-project"));

        var result = ProjectWritePolicy.AuthorizeSession(
            Path.Combine(Path.GetTempPath(), "other-project"),
            _ => true);

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be("ProjectRootNotAllowlisted");
    }

    [Fact]
    public void AuthorizeSession_WhenGrantIsMissing_ShouldReturnInteractiveRecovery()
    {
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, null);
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, null);

        var result = ProjectWritePolicy.AuthorizeSession(
            Path.Combine(Path.GetTempPath(), "wpf-project"),
            _ => false);

        result.Code.Should().Be("InteractiveConsentRequired");
        result.RepairSuggestion.Should().Contain("request_session_access");
        result.RepairSuggestion.Should().Contain("project-write");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        internal EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _original);
    }
}
