using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SignaturePolicySourceContractTests
{
    [Fact]
    public void DllPathValidator_ShouldUseWinVerifyTrustForReleaseAuthenticodeVerification()
    {
        var content = ReadDllPathValidatorSource();

        content.Should().Contain("WinVerifyTrust(",
            "release DLL validation should verify the signed PE file itself instead of trusting only the signer certificate metadata");
        content.Should().Contain("VerifyFileAuthenticodeTrust(filePath)",
            "the Authenticode file-trust check should run before certificate-chain inspection so tampered signed files are rejected");
    }

    [Fact]
    public void DllPathValidator_ShouldNotReuseTransportCertificateThumbprintForDllSignaturePinning()
    {
        var content = ReadDllPathValidatorSource();

        content.Should().NotContain("WPFDEVTOOLS_CERT_THUMBPRINT",
            "the transport TLS certificate pin must not double as the runtime DLL signer policy for injected payload validation");
    }

    [Fact]
    public void DllPathValidator_ShouldNotTrustInstallDirectoryManifestSignerMetadataForRuntimePinning()
    {
        var content = ReadDllPathValidatorSource();

        content.Should().Contain("Environment.ProcessPath",
            "runtime DLL signer pinning should fall back to the currently running signed MCP server executable when no explicit env pin is provided");
        content.Should().NotContain("manifest.json",
            "runtime DLL signer pinning must not trust mutable install-directory manifest metadata as the authoritative signer pin source");
    }

    private static string ReadDllPathValidatorSource()
    {
        return string.Concat(
            File.ReadAllText(TestRepositoryPaths.GetRepoFilePath("src/WpfDevTools.Mcp.Server/Tools/DllPathValidator.cs")),
            File.ReadAllText(TestRepositoryPaths.GetRepoFilePath("src/WpfDevTools.Mcp.Server/Tools/DllPathValidator.WinTrust.cs")));
    }
}
