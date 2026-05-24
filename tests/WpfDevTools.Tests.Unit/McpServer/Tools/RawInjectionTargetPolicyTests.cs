using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class RawInjectionTargetPolicyTests
{
    [Theory]
    [InlineData(@"\\server\share\Target.exe")]
    [InlineData(@"\\?\UNC\server\share\Target.exe")]
    public void Authorize_WhenRawInjectionTargetPathIsNetworkPath_ShouldFailClosed(string targetPath)
    {
        var authorization = RawInjectionTargetPolicy.Authorize(
            CreateProcessInfo(targetPath),
            AppContext.BaseDirectory,
            configuredAllowedTargets: null,
            tryResolvePhysicalPath: path => path);

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("local absolute path");
    }

    [Theory]
    [InlineData(@"\\server\share\Target.exe")]
    [InlineData(@"\\?\UNC\server\share\Target.exe")]
    public void Authorize_WhenRawInjectionAllowlistContainsNetworkPath_ShouldFailClosed(string configuredAllowedTarget)
    {
        var authorization = RawInjectionTargetPolicy.Authorize(
            CreateProcessInfo(@"C:\Allowed\Target.exe"),
            AppContext.BaseDirectory,
            configuredAllowedTargets: configuredAllowedTarget,
            tryResolvePhysicalPath: path => path);

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("Invalid raw injection allowlist configuration");
    }

    private static WpfProcessInfo CreateProcessInfo(string executablePath)
    {
        return new WpfProcessInfo
        {
            ProcessId = 12345,
            ProcessName = "TargetApp",
            WindowTitle = "TargetApp",
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true,
            ExecutablePath = executablePath
        };
    }
}
