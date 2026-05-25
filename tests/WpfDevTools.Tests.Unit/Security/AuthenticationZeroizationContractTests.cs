using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public sealed class AuthenticationZeroizationContractTests
{
    [Fact]
    public void ResponseCalculator_ShouldZeroExpectedHmacAndStoredSecret()
    {
        var source = ReadSource("src/WpfDevTools.Shared/Security/ResponseCalculator.cs");

        source.Should().Contain("CryptographicOperations.ZeroMemory(expected)",
            "VerifyResponse computes an expected HMAC that must not remain in managed memory after comparison");
        source.Should().Contain("CryptographicOperations.ZeroMemory(_sharedSecret)",
            "the calculator keeps a defensive copy of the shared secret and must wipe it on dispose");
    }

    [Fact]
    public void NamedPipeClientAuthentication_ShouldZeroChallengeResponseAndSecretCopies()
    {
        var source = ReadSource("src/WpfDevTools.Mcp.Server/NamedPipeClient.Transport.cs");

        source.Should().Contain("CryptographicOperations.ZeroMemory(challenge)");
        source.Should().Contain("CryptographicOperations.ZeroMemory(response)");
        source.Should().Contain("CryptographicOperations.ZeroMemory(secretCopy)");
    }

    [Fact]
    public void InspectorHostAuthentication_ShouldZeroChallengeResponseAndSecretCopies()
    {
        var source = ReadSource("src/WpfDevTools.Inspector/Host/InspectorHostSecurity.cs");

        source.Should().Contain("CryptographicOperations.ZeroMemory(challenge)");
        source.Should().Contain("CryptographicOperations.ZeroMemory(response)");
        source.Should().Contain("CryptographicOperations.ZeroMemory(secretCopy)");
    }

    private static string ReadSource(string relativePath)
        => File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));
}
