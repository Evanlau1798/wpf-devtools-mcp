using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Security;
using WpfDevTools.Tests.Unit.TestSupport;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.Inspector;

[Collection("InspectorHostLifecycle")]
public sealed class InspectorHostPlaintextSecurityTests
{
    [Fact]
    public void Start_WhenPlaintextTransportHasNoUnsafeOptIn_ShouldThrow()
    {
        using var policy = InspectorHost.BeginUnsafePlaintextPolicyTestScope(static () => false);
        using var host = new InspectorHost(NextSyntheticProcessId());

        var act = () => host.Start();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unsafe plaintext*");
    }

    [Fact]
    public void Start_WhenPlaintextTransportHasUnsafeOptIn_ShouldStart()
    {
        using var policy = InspectorHost.BeginUnsafePlaintextPolicyTestScope(static () => true);
        using var host = new InspectorHost(NextSyntheticProcessId());

        host.Start();

        host.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void Start_WhenOnlyAuthenticationIsConfigured_ShouldThrow()
    {
        using var policy = InspectorHost.BeginUnsafePlaintextPolicyTestScope(static () => false);
        using var authManager = CreateAuthenticationManager();
        using var host = new InspectorHost(NextSyntheticProcessId(), authManager);

        var act = () => host.Start();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*authentication and TLS*");
    }

    [Fact]
    public void Start_WhenOnlyTlsIsConfigured_ShouldThrow()
    {
        using var policy = InspectorHost.BeginUnsafePlaintextPolicyTestScope(static () => false);
        using var certDirectory = new CertificateDirectoryScope();
        using var host = new InspectorHost(
            NextSyntheticProcessId(),
            authManager: null,
            certManager: new CertificateManager(certDirectory.Path));

        var act = () => host.Start();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*authentication and TLS*");
    }

    [Fact]
    public void Start_WhenAuthenticationAndTlsAreConfigured_ShouldStart()
    {
        using var policy = InspectorHost.BeginUnsafePlaintextPolicyTestScope(static () => false);
        using var authManager = CreateAuthenticationManager();
        using var certDirectory = new CertificateDirectoryScope();
        using var host = new InspectorHost(
            NextSyntheticProcessId(),
            authManager,
            new CertificateManager(certDirectory.Path));

        host.Start();

        host.IsRunning.Should().BeTrue();
    }

    private static AuthenticationManager CreateAuthenticationManager()
    {
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes);
        Array.Clear(secretBytes);
        return new AuthenticationManager(() => secret);
    }

    private sealed class CertificateDirectoryScope : IDisposable
    {
        public CertificateDirectoryScope()
        {
            Path = System.IO.Path.Combine(
                TestRepositoryPaths.GetRepoFilePath("tmp"),
                $"InspectorHostPlaintextSecurityTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
