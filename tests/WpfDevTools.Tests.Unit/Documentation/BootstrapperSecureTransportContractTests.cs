using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BootstrapperSecureTransportContractTests
{
    [Fact]
    public void BootstrapEntry_ShouldCarrySecureTransportParametersThroughManagedBootstrap()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().Contain("authSecretFile",
            "the native bootstrapper should receive a secret file path instead of a raw secret in bootstrap parameters");
        content.Should().NotContain("pair.rfind(L\"authSecretBase64=\", 0)",
            "raw authentication secrets must not be accepted through the injected bootstrap argument string");
        content.Should().Contain("certDirectory",
            "the native bootstrapper must preserve the certificate directory when secure pipe TLS is enabled");
        content.Should().Contain("BuildManagedParams",
            "the managed inspector bootstrap needs the same secure transport parameters after the native hand-off");
        content.Should().Contain("ParseKeyValueParams",
            "secure transport bootstrap arguments are now carried as key=value pairs instead of the legacy two-token format");
    }

    [Fact]
    public void BootstrapperExitCodes_ShouldIncludeSpecificAuthSecretLoadFailure()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/exit_codes.h"));

        content.Should().MatchRegex(@"AuthSecretLoadFailed\s*=\s*0x15");
    }

    [Fact]
    public void BootstrapEntry_ShouldReturnAuthSecretLoadFailedWhenSecretFileCannotBeRead()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().MatchRegex(@"if\s*\(\s*!LoadAuthSecretFromFile\(config\)\s*\)\s*return\s+ExitCodes::AuthSecretLoadFailed\s*;",
            "auth secret file read failures should not be misclassified as inspector path failures");
    }

    [Fact]
    public void BootstrapEntry_ShouldLogAuthSecretFileDeletionFailure()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().Contain("DeleteAuthSecretFile(config.AuthSecretFile)");
        content.Should().Contain("GetLastError()");
        content.Should().Contain("OutputDebugStringW");
        content.Should().NotContain("DeleteFileW(config.AuthSecretFile.c_str());",
            "auth secret file deletion failures should produce a diagnostic signal");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
