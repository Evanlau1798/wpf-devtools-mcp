using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SessionAccessRequestServiceTests
{
    private DateTimeOffset _now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequestAsync_WhenUserAccepts_ShouldGrantImmediately()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);
        ElicitRequestParams? prompt = null;

        var result = await service.RequestAsync(
            new SessionAccessRequest(
                [SessionAccessCapabilities.Screenshot],
                ProcessId: 123,
                ProjectRoot: null,
                PackRef: null,
                Reason: "Inspect the rendered UI.",
                Lifetime: SessionAccessLifetime.Session),
            supportsElicitation: true,
            (request, _) =>
            {
                prompt = request;
                return Task.FromResult(Accepted());
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Status.Should().Be("granted");
        result.Scopes.Should().ContainSingle();
        store.HasGrant(result.Scopes[0]).Should().BeTrue();
        prompt.Should().NotBeNull();
        prompt!.Message.Should().Contain("screenshot");
        prompt.Message.Should().Contain("Agent-provided reason (untrusted)");
        prompt.RequestedSchema!.Required.Should().Contain("confirm");
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task RequestAsync_WhenUserDoesNotAccept_ShouldNotGrant(string action)
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);

        var result = await service.RequestAsync(
            ProcessRequest(SessionAccessCapabilities.SensitiveRead),
            supportsElicitation: true,
            (_, _) => Task.FromResult(new ElicitResult { Action = action }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(action);
        result.Scopes.Should().OnlyContain(scope => !store.HasGrant(scope));
    }

    [Fact]
    public async Task RequestAsync_WhenClientDoesNotSupportElicitation_ShouldFailClosed()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);
        var elicitationCalled = false;

        var result = await service.RequestAsync(
            ProcessRequest(SessionAccessCapabilities.TargetConnect),
            supportsElicitation: false,
            (_, _) =>
            {
                elicitationCalled = true;
                return Task.FromResult(Accepted());
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("InteractiveConsentUnavailable");
        elicitationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RequestAsync_WhenAcceptResponseDoesNotConfirm_ShouldFailClosed()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);

        var result = await service.RequestAsync(
            ProcessRequest(SessionAccessCapabilities.TargetConnect),
            supportsElicitation: true,
            (_, _) => Task.FromResult(new ElicitResult { Action = "accept" }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Scopes.Should().OnlyContain(scope => !store.HasGrant(scope));
    }

    [Fact]
    public async Task RequestAsync_WhenRawInjectionRequestsSessionLifetime_ShouldRejectBeforePrompt()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);
        var elicitationCalled = false;

        var result = await service.RequestAsync(
            ProcessRequest(SessionAccessCapabilities.RawInjection, SessionAccessLifetime.Session),
            supportsElicitation: true,
            (_, _) =>
            {
                elicitationCalled = true;
                return Task.FromResult(Accepted());
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidAccessLifetime");
        elicitationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RequestAsync_ShouldBoundAndFlattenUntrustedReason()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);
        ElicitRequestParams? prompt = null;
        var reason = new string('x', 400) + "\r\nspoof";

        await service.RequestAsync(
            ProcessRequest(SessionAccessCapabilities.Screenshot) with { Reason = reason },
            supportsElicitation: true,
            (request, _) =>
            {
                prompt = request;
                return Task.FromResult(Accepted());
            },
            CancellationToken.None);

        prompt!.Message.Should().NotContain("\r").And.NotContain("\n");
        prompt.Message.Length.Should().BeLessThan(900);
        prompt.Message.Should().NotContain("spoof");
    }

    [Fact]
    public async Task RequestAsync_WhenMatchingGrantAlreadyExists_ShouldNotPromptAgain()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);
        var request = ProcessRequest(SessionAccessCapabilities.Screenshot);

        var first = await service.RequestAsync(
            request,
            supportsElicitation: true,
            (_, _) => Task.FromResult(Accepted()),
            CancellationToken.None);
        var promptCount = 0;
        var second = await service.RequestAsync(
            request,
            supportsElicitation: true,
            (_, _) =>
            {
                promptCount++;
                return Task.FromResult(Accepted());
            },
            CancellationToken.None);

        first.Success.Should().BeTrue();
        second.Status.Should().Be("already-granted");
        promptCount.Should().Be(0);
    }

    [Fact]
    public async Task RequestAsync_ShouldRateLimitRepeatedConsentPrompts()
    {
        using var store = new SessionAccessGrantStore(() => _now);
        var service = CreateService(store);
        var promptCount = 0;

        for (var index = 0; index < 4; index++)
        {
            var result = await service.RequestAsync(
                ProcessRequest(SessionAccessCapabilities.Screenshot),
                supportsElicitation: true,
                (_, _) =>
                {
                    promptCount++;
                    return Task.FromResult(new ElicitResult { Action = "decline" });
                },
                CancellationToken.None);

            if (index == 3)
            {
                result.ErrorCode.Should().Be("ConsentPromptRateLimited");
            }
        }

        promptCount.Should().Be(3);
    }

    private SessionAccessRequestService CreateService(SessionAccessGrantStore store)
        => new(
            store,
            new SessionAccessScopeResolver(
                processId => processId == 123
                    ? new TargetProcessIdentity(123, 100, @"G:\apps\sample\Sample.exe")
                    : null,
                () => null),
            () => _now);

    private static SessionAccessRequest ProcessRequest(
        string capability,
        SessionAccessLifetime lifetime = SessionAccessLifetime.Session)
        => new(
            [capability],
            ProcessId: 123,
            ProjectRoot: null,
            PackRef: null,
            Reason: "Required for the requested workflow.",
            Lifetime: lifetime);

    private static ElicitResult Accepted()
        => new()
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement>
            {
                ["confirm"] = JsonSerializer.SerializeToElement(true)
            }
        };
}
