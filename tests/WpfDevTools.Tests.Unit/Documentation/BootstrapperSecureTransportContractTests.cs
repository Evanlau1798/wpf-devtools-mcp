using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BootstrapperSecureTransportContractTests
{
    [Fact]
    public void BootstrapEntry_ShouldCarrySecureTransportParametersThroughManagedBootstrap()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().Contain("authSecretBase64",
            "the native bootstrapper must preserve the shared auth secret when secure pipe auth is enabled");
        content.Should().Contain("certDirectory",
            "the native bootstrapper must preserve the certificate directory when secure pipe TLS is enabled");
        content.Should().Contain("BuildManagedParams",
            "the managed inspector bootstrap needs the same secure transport parameters after the native hand-off");
        content.Should().Contain("ParseKeyValueParams",
            "secure transport bootstrap arguments are now carried as key=value pairs instead of the legacy two-token format");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}