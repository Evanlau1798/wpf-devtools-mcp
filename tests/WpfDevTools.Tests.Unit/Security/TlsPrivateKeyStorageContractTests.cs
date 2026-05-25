using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public class TlsPrivateKeyStorageContractTests
{
    [Fact]
    public void SslStreamDiagnosticFixture_ShouldNotUseExportableMachineKeyImportAsCompatibilityPattern()
    {
        var content = File.ReadAllText(GetRepoPath(
            "tests/WpfDevTools.Tests.Unit/Security/SslStreamDiagnosticTests.cs"));

        content.Should().NotContain(
            "X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet",
            "TLS diagnostics should not document the production-rejected exportable machine-key import as the SslStream compatibility pattern");
        content.Should().NotContain(
            "must export/re-import PFX for SslStream compatibility",
            "test comments should not imply production TLS certificates need exportable persisted machine keys");
    }

    private static string GetRepoPath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
