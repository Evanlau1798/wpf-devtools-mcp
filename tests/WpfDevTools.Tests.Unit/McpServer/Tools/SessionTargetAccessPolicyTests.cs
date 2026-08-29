using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class SessionTargetAccessPolicyTests
{
    [Fact]
    public void AuthorizeTarget_WhenAllowlistIsUnsetAndExactGrantExists_ShouldAllow()
    {
        using var store = CreateGrantedStore(SessionAccessCapabilities.TargetConnect, out var service);

        var result = SessionTargetAccessPolicy.AuthorizeTarget(
            ProcessInfo(), service, configuredAllowedTargets: null, Path.GetFullPath);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeTarget_WhenConfiguredAllowlistExcludesTarget_ShouldIgnoreGrant()
    {
        using var store = CreateGrantedStore(SessionAccessCapabilities.TargetConnect, out var service);

        var result = SessionTargetAccessPolicy.AuthorizeTarget(
            ProcessInfo(), service, @"G:\apps\other\Other.exe", Path.GetFullPath);

        result.IsAllowed.Should().BeFalse();
        result.Error.Should().Contain("allowlist");
    }

    [Fact]
    public void RawInjectionGrant_ShouldBeConsumedAtMostOnce()
    {
        using var store = CreateGrantedStore(
            SessionAccessCapabilities.RawInjection,
            out var service,
            SessionAccessLifetime.Once);
        var process = ProcessInfo();

        SessionTargetAccessPolicy.IsRawInjectionAllowed(
            process, service, configuredAllowedTargets: null, Path.GetFullPath).Should().BeTrue();
        SessionTargetAccessPolicy.ConsumeRawInjection(
            process, service, configuredAllowedTargets: null, Path.GetFullPath).Should().BeTrue();
        SessionTargetAccessPolicy.ConsumeRawInjection(
            process, service, configuredAllowedTargets: null, Path.GetFullPath).Should().BeFalse();
    }

    [Fact]
    public void RawInjection_WhenConfiguredAllowlistExcludesTarget_ShouldIgnoreGrant()
    {
        using var store = CreateGrantedStore(
            SessionAccessCapabilities.RawInjection,
            out var service,
            SessionAccessLifetime.Once);

        SessionTargetAccessPolicy.IsRawInjectionAllowed(
            ProcessInfo(), service, @"G:\apps\other\Other.exe", Path.GetFullPath).Should().BeFalse();
    }

    private static SessionAccessGrantStore CreateGrantedStore(
        string capability,
        out SessionAccessRequestService service,
        SessionAccessLifetime lifetime = SessionAccessLifetime.Session)
    {
        var store = new SessionAccessGrantStore();
        service = new SessionAccessRequestService(
            store,
            new SessionAccessScopeResolver(
                processId => processId == 123
                    ? new TargetProcessIdentity(123, 100, @"G:\apps\sample\Sample.exe")
                    : null,
                () => null));
        store.Grant(
            SessionAccessScope.ForProcess(capability, 123, 100, @"G:\apps\sample\Sample.exe"),
            lifetime).Should().BeTrue();
        return store;
    }

    private static WpfProcessInfo ProcessInfo()
        => new()
        {
            ProcessId = 123,
            ProcessName = "Sample",
            WindowTitle = "Sample",
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true,
            ExecutablePath = @"G:\apps\sample\Sample.exe"
        };
}
