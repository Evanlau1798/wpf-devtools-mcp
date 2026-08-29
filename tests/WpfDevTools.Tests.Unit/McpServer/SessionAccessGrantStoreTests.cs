using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SessionAccessGrantStoreTests
{
    private DateTimeOffset _now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SessionGrant_ShouldAuthorizeMatchingScopeUntilExpiry()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var scope = ProcessScope(SessionAccessCapabilities.Screenshot, startTimeUtcTicks: 100);

        store.Grant(scope, SessionAccessLifetime.Session).Should().BeTrue();
        store.HasGrant(scope).Should().BeTrue();

        _now = _now.AddMinutes(30);

        store.HasGrant(scope).Should().BeFalse();
    }

    [Fact]
    public void OneTimeGrant_ShouldBeConsumedExactlyOnce()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var scope = ProjectScope(@"G:\apps\sample");

        store.Grant(scope, SessionAccessLifetime.Once).Should().BeTrue();
        store.TryConsume(scope).Should().BeTrue();
        store.TryConsume(scope).Should().BeFalse();
    }

    [Fact]
    public void RawInjection_ShouldRejectSessionLifetime()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var scope = ProcessScope(SessionAccessCapabilities.RawInjection, startTimeUtcTicks: 100);

        store.Grant(scope, SessionAccessLifetime.Session).Should().BeFalse();
        store.HasGrant(scope).Should().BeFalse();
    }

    [Fact]
    public void ProcessGrant_ShouldRejectReusedPidWithDifferentStartTime()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var granted = ProcessScope(SessionAccessCapabilities.TargetConnect, startTimeUtcTicks: 100);
        var reusedPid = ProcessScope(SessionAccessCapabilities.TargetConnect, startTimeUtcTicks: 101);

        store.Grant(granted, SessionAccessLifetime.Session).Should().BeTrue();

        store.HasGrant(reusedPid).Should().BeFalse();
    }

    [Fact]
    public void ProjectGrant_ShouldRequireExactNormalizedRoot()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var granted = ProjectScope(@"G:\apps\sample");

        store.Grant(granted, SessionAccessLifetime.Session).Should().BeTrue();

        store.HasGrant(ProjectScope(@"g:\apps\sample\")).Should().BeTrue();
        store.HasGrant(ProjectScope(@"G:\apps\sample-child")).Should().BeFalse();
    }

    [Fact]
    public void Dispose_ShouldRevokeAllGrants()
    {
        var store = new SessionAccessGrantStore(() => _now);
        var scope = ProcessScope(SessionAccessCapabilities.SensitiveRead, startTimeUtcTicks: 100);
        store.Grant(scope, SessionAccessLifetime.Session).Should().BeTrue();

        store.Dispose();

        store.HasGrant(scope).Should().BeFalse();
    }

    private static SessionAccessScope ProcessScope(string capability, long startTimeUtcTicks)
        => SessionAccessScope.ForProcess(
            capability,
            processId: 123,
            processStartTimeUtcTicks: startTimeUtcTicks,
            executablePath: @"G:\apps\sample\Sample.exe");

    private static SessionAccessScope ProjectScope(string projectRoot)
        => SessionAccessScope.ForProject(SessionAccessCapabilities.ProjectWrite, projectRoot);
}
