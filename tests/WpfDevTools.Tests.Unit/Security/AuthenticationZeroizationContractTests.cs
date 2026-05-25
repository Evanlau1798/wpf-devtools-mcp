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

    [Fact]
    public void Net48CryptographicOperationsPolyfill_ShouldExposeZeroMemory()
    {
        var source = ReadSource("src/WpfDevTools.Shared/Polyfills/CryptographicOperations.cs");

        source.Should().Contain("public static void ZeroMemory(byte[] buffer)",
            "net48 builds use this polyfill and must support the same zeroization calls as the shared security code");
        source.Should().Contain("Array.Clear(buffer, 0, buffer.Length)",
            "the fallback implementation should still clear the managed buffer on .NET Framework");
    }

    private static string ReadSource(string relativePath)
        => File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));
}
