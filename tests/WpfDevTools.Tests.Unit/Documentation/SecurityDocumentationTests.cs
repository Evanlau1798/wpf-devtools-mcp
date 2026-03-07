using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class SecurityDocumentationTests
{
    [Theory]
    [InlineData("WPFDEVTOOLS_AUTH_SECRET")]
    [InlineData("WPFDEVTOOLS_CERT_DIR")]
    [InlineData("WPFDEVTOOLS_CERT_THUMBPRINT")]
    public void Documentation_ShouldMentionSupportedEnvironmentVariables(string variableName)
    {
        var content = ReadDocumentation();

        content.Should().Contain(variableName);
    }

    [Theory]
    [InlineData("WPFDEVTOOLS_REQUIRE_SIGNATURE")]
    [InlineData("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK")]
    [InlineData("WPFDEVTOOLS_ENCRYPTION_MODE")]
    [InlineData("WPFDEVTOOLS_MAX_SESSIONS")]
    [InlineData("WPFDEVTOOLS_RATE_LIMIT")]
    [InlineData("WPFDEVTOOLS_AUDIT_LOG_PATH")]
    public void Documentation_ShouldNotClaimUnsupportedEnvironmentVariables(string variableName)
    {
        var content = ReadDocumentation();

        content.Should().NotContain(variableName);
    }

    private static string ReadDocumentation()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var security = File.ReadAllText(GetRepoFilePath("SECURITY.md"));
        return readme + Environment.NewLine + security;
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
