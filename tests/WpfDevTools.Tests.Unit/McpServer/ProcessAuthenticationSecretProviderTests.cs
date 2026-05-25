using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ProcessAuthenticationSecretProviderTests
{
    [Fact]
    public void GetAuthenticationSecretBase64_WithDifferentProcesses_ShouldReturnDifferentSecrets()
    {
        using var rootAuthentication = CreateRootAuthenticationManager();
        var provider = new ProcessAuthenticationSecretProvider(rootAuthentication);

        var firstSecret = provider.GetAuthenticationSecretBase64(101);
        var secondSecret = provider.GetAuthenticationSecretBase64(202);

        firstSecret.Should().NotBeNullOrWhiteSpace();
        secondSecret.Should().NotBeNullOrWhiteSpace();
        firstSecret.Should().NotBe(secondSecret);
        Convert.FromBase64String(firstSecret!).Should().HaveCount(32);
        Convert.FromBase64String(secondSecret!).Should().HaveCount(32);
    }

    [Fact]
    public void GetAuthenticationSecretBase64_WithSameProcess_ShouldBeStable()
    {
        using var rootAuthentication = CreateRootAuthenticationManager();
        var provider = new ProcessAuthenticationSecretProvider(rootAuthentication);

        provider.GetAuthenticationSecretBase64(303)
            .Should().Be(provider.GetAuthenticationSecretBase64(303));
    }

    [Fact]
    public void GetAuthenticationSecretBase64_WithSameProcessDifferentPipeNames_ShouldReturnDifferentSecrets()
    {
        using var rootAuthentication = CreateRootAuthenticationManager();
        var provider = new ProcessAuthenticationSecretProvider(rootAuthentication);

        var firstSecret = provider.GetAuthenticationSecretBase64(303, "WpfDevTools_303_first");
        var secondSecret = provider.GetAuthenticationSecretBase64(303, "WpfDevTools_303_second");

        firstSecret.Should().NotBeNullOrWhiteSpace();
        secondSecret.Should().NotBeNullOrWhiteSpace();
        firstSecret.Should().NotBe(secondSecret);
    }

    [Fact]
    public void GetAuthenticationSecretBase64_WithSamePidAndPipeButDifferentStartTimes_ShouldReturnDifferentSecrets()
    {
        using var rootAuthentication = CreateRootAuthenticationManager();
        var startTimeTicks = 1000L;
        var provider = new ProcessAuthenticationSecretProvider(
            rootAuthentication,
            processId => new ProcessAuthenticationSecretProvider.ProcessIdentity(
                processId,
                startTimeTicks));

        var firstSecret = provider.GetAuthenticationSecretBase64(303, "WpfDevTools_303_reused");
        startTimeTicks = 2000L;
        var secondSecret = provider.GetAuthenticationSecretBase64(303, "WpfDevTools_303_reused");

        firstSecret.Should().NotBeNullOrWhiteSpace();
        secondSecret.Should().NotBeNullOrWhiteSpace();
        firstSecret.Should().NotBe(secondSecret,
            "PID reuse should not let an old process-scoped authentication secret authenticate a new process instance");
    }

    [Fact]
    public void GetAuthenticationSecretBase64_WithDifferentProcesses_ShouldPreventCrossTargetResponses()
    {
        using var rootAuthentication = CreateRootAuthenticationManager();
        var provider = new ProcessAuthenticationSecretProvider(rootAuthentication);
        var firstSecret = Convert.FromBase64String(provider.GetAuthenticationSecretBase64(101)!);
        var secondSecret = Convert.FromBase64String(provider.GetAuthenticationSecretBase64(202)!);
        var challenge = RandomNumberGenerator.GetBytes(32);

        using var firstCalculator = new ResponseCalculator(firstSecret);
        using var secondCalculator = new ResponseCalculator(secondSecret);
        var firstResponse = firstCalculator.ComputeResponse(challenge);

        secondCalculator.VerifyResponse(challenge, firstResponse).Should().BeFalse();
    }

    [Fact]
    public void SessionManager_GetAuthenticationSecretBase64_ShouldScopeSecretToProcess()
    {
        using var rootAuthentication = CreateRootAuthenticationManager();
        using var sessionManager = new SessionManager(authManager: rootAuthentication);

        sessionManager.GetAuthenticationSecretBase64(101)
            .Should().NotBe(sessionManager.GetAuthenticationSecretBase64(202));
    }

    [Fact]
    public void SessionManager_GetAuthenticationSecretBase64_WithPipeName_ShouldScopeSecretToPipeInstance()
    {
        using var rootAuthentication = CreateRootAuthenticationManager();
        using var sessionManager = new SessionManager(authManager: rootAuthentication);

        sessionManager.GetAuthenticationSecretBase64(101, "WpfDevTools_101_first")
            .Should().NotBe(sessionManager.GetAuthenticationSecretBase64(101, "WpfDevTools_101_second"));
    }

    private static AuthenticationManager CreateRootAuthenticationManager()
    {
        var rootSecret = new byte[32];
        for (var index = 0; index < rootSecret.Length; index++)
        {
            rootSecret[index] = (byte)(index + 1);
        }

        return new AuthenticationManager(() => Convert.ToBase64String(rootSecret));
    }
}
