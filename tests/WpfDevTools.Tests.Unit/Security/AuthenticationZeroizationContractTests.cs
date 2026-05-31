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
    public void CertificateManager_ShouldZeroGeneratedPasswordAndExportedPfxBytes()
    {
        var source = ReadSource("src/WpfDevTools.Shared/Security/CertificateManager.cs");

        source.Should().Contain("CryptographicOperations.ZeroMemory(randomBytes)",
            "the temporary random password bytes are converted to a string for the X509 API boundary and should then be wiped");
        source.Should().Contain("CryptographicOperations.ZeroMemory(pfxBytes)",
            "the exported encrypted PFX payload should be wiped after it is written to disk");
    }

    [Fact]
    public void AuthenticationManager_ShouldZeroRejectedDecodedEnvironmentSecret()
    {
        var source = ReadSource("src/WpfDevTools.Shared/Security/AuthenticationManager.cs");

        source.Should().Contain("ClearSecret(decoded)",
            "decoded environment secret bytes must be wiped before throwing for a short secret");
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
