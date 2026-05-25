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

        FindRejectedKeyStorageFlags(content).Should().BeEmpty(
            "TLS diagnostics should not document production-rejected exportable or machine-key imports as the SslStream compatibility pattern");
        content.Should().NotContain(
            "must export/re-import PFX for SslStream compatibility",
            "test comments should not imply production TLS certificates need exportable persisted machine keys");
    }

    [Fact]
    public void RejectedKeyStorageFlagScanner_ShouldDetectSeparatedRejectedFlags()
    {
        const string content = """
            var flags =
                X509KeyStorageFlags.Exportable |
                X509KeyStorageFlags.MachineKeySet;
            """;

        FindRejectedKeyStorageFlags(content)
            .Should().BeEquivalentTo("X509KeyStorageFlags.Exportable", "X509KeyStorageFlags.MachineKeySet");
    }

    private static string[] FindRejectedKeyStorageFlags(string content)
    {
        string[] rejectedFlags =
        [
            "X509KeyStorageFlags.Exportable",
            "X509KeyStorageFlags.MachineKeySet"
        ];

        return rejectedFlags
            .Where(flag => content.Contains(flag, StringComparison.Ordinal))
            .ToArray();
    }

    private static string GetRepoPath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
