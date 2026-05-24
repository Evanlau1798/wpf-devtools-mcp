using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class McpTargetPolicyTests
{
    [Fact]
    public void Authorize_WhenNoTargetAllowlistIsConfigured_ShouldFailClosed()
    {
        var authorization = McpTargetPolicy.Authorize(
            CreateProcessInfo(@"C:\Allowed\Target.exe"),
            configuredAllowedTargets: null,
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("target allowlist is not configured");
        authorization.Hint.Should().Contain(McpServerConfiguration.AllowedTargetsEnvVar);
    }

    [Fact]
    public void Authorize_WhenTargetAllowlistIsConfiguredAndTargetMatches_ShouldAllow()
    {
        const string targetPath = @"C:\Allowed\Target.exe";

        var authorization = McpTargetPolicy.Authorize(
            CreateProcessInfo(targetPath),
            configuredAllowedTargets: targetPath,
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Authorize_WhenTargetAllowlistIsConfiguredAndTargetDiffers_ShouldDeny()
    {
        var authorization = McpTargetPolicy.Authorize(
            CreateProcessInfo(@"C:\Denied\Target.exe", processName: "DeniedApp"),
            configuredAllowedTargets: @"C:\Allowed\Target.exe",
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("blocked by the MCP target allowlist");
        authorization.Hint.Should().Contain(McpServerConfiguration.AllowedTargetsEnvVar);
    }

    [Fact]
    public void Authorize_WhenTargetAllowlistIsConfiguredAndTargetPathIsMissing_ShouldFailClosed()
    {
        var authorization = McpTargetPolicy.Authorize(
            CreateProcessInfo(executablePath: null),
            configuredAllowedTargets: @"C:\Allowed\Target.exe",
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("target executable path is missing");
        authorization.Hint.Should().Contain(McpServerConfiguration.AllowedTargetsEnvVar);
    }

    [Theory]
    [InlineData(@"relative\Target.exe")]
    [InlineData(@"relative\Target.exe;C:\Allowed\Target.exe")]
    public void Authorize_WhenTargetAllowlistContainsInvalidEntries_ShouldFailClosed(string configuredAllowedTargets)
    {
        var authorization = McpTargetPolicy.Authorize(
            CreateProcessInfo(@"C:\Allowed\Target.exe"),
            configuredAllowedTargets,
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("Invalid MCP target allowlist configuration");
        authorization.Hint.Should().Contain(McpServerConfiguration.AllowedTargetsEnvVar);
    }

    [Theory]
    [InlineData(@"\\server\share\Target.exe")]
    [InlineData(@"\\?\UNC\server\share\Target.exe")]
    public void Authorize_WhenTargetAllowlistContainsNetworkPath_ShouldFailClosed(string configuredAllowedTargets)
    {
        var authorization = McpTargetPolicy.Authorize(
            CreateProcessInfo(configuredAllowedTargets),
            configuredAllowedTargets,
            path => path);

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("Invalid MCP target allowlist configuration");
        authorization.Error.Should().Contain("exact local absolute executable path");
        authorization.Hint.Should().Contain(McpServerConfiguration.AllowedTargetsEnvVar);
    }

    [Fact]
    public void Authorize_WhenResolvedTargetPathIsNetworkPath_ShouldFailClosed()
    {
        var authorization = McpTargetPolicy.Authorize(
            CreateProcessInfo(@"C:\Allowed\Target.exe"),
            configuredAllowedTargets: @"C:\Allowed\Target.exe",
            _ => @"\\server\share\Target.exe");

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("Invalid MCP target allowlist configuration");
        authorization.Error.Should().Contain("exact local absolute executable path");
        authorization.Hint.Should().Contain(McpServerConfiguration.AllowedTargetsEnvVar);
    }

    private static WpfProcessInfo CreateProcessInfo(string? executablePath, string processName = "TargetApp")
    {
        return new WpfProcessInfo
        {
            ProcessId = 12345,
            ProcessName = processName,
            WindowTitle = processName,
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true,
            ExecutablePath = executablePath
        };
    }
}
