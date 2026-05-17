using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ConfigurationReferenceDocumentationTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Theory]
    [InlineData("docfx/reference/configuration.md")]
    [InlineData("docfx/zh-tw/reference/configuration.md")]
    public void ConfigurationReference_ShouldDocumentShippingEnvironmentVariables(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        var expectedVariables = new[]
        {
            "WPFDEVTOOLS_AUTH_SECRET",
            "WPFDEVTOOLS_CERT_DIR",
            "WPFDEVTOOLS_CERT_THUMBPRINT",
            "WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS",
            "WPFDEVTOOLS_RATE_LIMIT_RPM",
            "WPFDEVTOOLS_TEXT_FALLBACK_MODE",
            "WPFDEVTOOLS_SKIP_ELEVATION",
            "WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
            "WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT",
            "WPFDEVTOOLS_CLAUDE_COMMAND_PATH",
            "WPFDEVTOOLS_CODEX_COMMAND_PATH"
        };

        foreach (var variableName in expectedVariables)
        {
            content.Should().Contain(variableName,
                $"{relativePath} should be the complete public configuration reference for shipping WPFDEVTOOLS_* variables");
        }

        content.Should().NotContain("WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH",
            $"{relativePath} should not list the removed elevated CLI command path opt-in as a shipping variable");
    }

    [Fact]
    public void EnglishConfigurationReference_ShouldDescribeSecureTransportVariablesAsOverrides()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/reference/configuration.md"));

        content.Should().Contain("Overrides the persisted/default HMAC authentication secret");
        content.Should().Contain("Overrides the default TLS certificate directory");
    }

    [Fact]
    public void EnglishConfigurationReference_ShouldDocumentRateLimitBounds()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/reference/configuration.md"));

        content.Should().Contain("default is 300");
        content.Should().Contain("values above 10000 are clamped to 10000");
        content.Should().Contain("internal per-process rate limiter cache is capped at 1000 entries");
    }

    [Fact]
    public void TraditionalChineseConfigurationReference_ShouldDescribeSecureTransportVariablesAsOverrides()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/reference/configuration.md"));

        content.Should().Contain("覆寫 persisted/default HMAC 驗證 secret");
        content.Should().Contain("覆寫預設 TLS certificate directory");
    }

    [Fact]
    public void TraditionalChineseConfigurationReference_ShouldDocumentRateLimitBounds()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/reference/configuration.md"));

        content.Should().Contain("預設值為 300");
        content.Should().Contain("超過 10000 的值會被 clamp 為 10000");
        content.Should().Contain("internal per-process rate limiter cache 上限為 1000 筆");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
