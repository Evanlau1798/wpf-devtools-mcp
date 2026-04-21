using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class SecurityDocumentationTests
{
    [Theory]
    [InlineData("WPFDEVTOOLS_AUTH_SECRET")]
    [InlineData("WPFDEVTOOLS_CERT_DIR")]
    [InlineData("WPFDEVTOOLS_CERT_THUMBPRINT")]
    [InlineData("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS")]
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

    [Theory]
    [InlineData("By default the server runs without authentication or encryption.")]
    [InlineData("If the variable is not set, authentication is disabled.")]
    [InlineData("Authentication and TLS are opt-in, not automatic.")]
    [InlineData("If the variable is absent, authentication is disabled.")]
    [InlineData("Authentication and TLS are opt-in.")]
    [InlineData("驗證與 TLS 都是 opt-in。")] 
    public void Documentation_ShouldNotDescribeInjectionTransportSecurityAsOptIn(string outdatedPhrase)
    {
        var content = ReadDocumentation();

        content.Should().NotContain(outdatedPhrase);
    }

    [Theory]
    [InlineData("README.md", "injection-based")]
    [InlineData("SECURITY.md", "injection-based")]
    [InlineData("docfx/production/security.md", "Injection-based")]
    [InlineData("docfx/zh-tw/production/security.md", "injection")]
    [InlineData("docfx/index.md", "persisted local HMAC secret and TLS")]
    [InlineData("docfx/zh-tw/index.md", "持久化的本機 HMAC secret 與 named-pipe TLS")]
    [InlineData("docfx/architecture/overview.md", "shipping injection path hardened by default")]
    [InlineData("docfx/zh-tw/architecture/overview.md", "正式發佈的 injection path 預設即為 hardened")]
    public void Documentation_ShouldDescribeDefaultInjectionTransportHardening(string relativePath, string expectedPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedPhrase,
            $"{relativePath} should explain that default hardening applies to the injection-based connect path");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs")]
    public void Documentation_ShouldDescribeRawInjectionOptInErrorContract(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS",
            $"{relativePath} should document the explicit raw injection allowlist");
        content.Should().Contain("SecurityError",
            $"{relativePath} should mention the security error surface for blocked raw injection");
        content.Should().Contain("requiresExplicitTargetOptIn",
            $"{relativePath} should document the machine-readable opt-in signal for blocked raw injection");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    [InlineData("src/WpfDevTools.Inspector.Sdk/README.md")]
    [InlineData("docfx/guides/troubleshooting.md")]
    [InlineData("docfx/zh-tw/guides/troubleshooting.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs")]
    public void Documentation_ShouldDescribeSdkWorkflowCompatibilityExplicitly(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("connect()",
            $"{relativePath} should mention the current MCP connect workflow when discussing SDK mode");
        content.Should().Contain("SDK",
            $"{relativePath} should mention SDK-hosted compatibility when discussing connect()");
    }

    [Fact]
    public void InspectorSdkReadme_ShouldDescribeMatchingTransportRequirement()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/README.md"));

        content.Should().Contain("can reuse",
            "the SDK README should describe the compatible SDK-hosted reuse path");
        content.Should().Contain("matching transport settings",
            "the SDK README should make the transport-matching requirement explicit");
        content.Should().Contain("can still time out",
            "the SDK README should describe that plaintext or unresponsive existing hosts may still time out before a transport mismatch can be proven");
        content.Should().Contain("must set both",
            "the SDK README should explain that partial SDK transport configuration is rejected");
        content.Should().Contain("InspectorSdk.Initialize()",
            "the SDK README should explain the exact SDK entrypoint affected by explicit transport hardening");
        content.Should().Contain("will not reuse",
            "the SDK README should explain that the default-hardened MCP server does not reuse plaintext SDK hosts");
    }

    [Theory]
    [InlineData("README.md", "both", "will not reuse")]
    [InlineData("SECURITY.md", "both", "will not reuse")]
    [InlineData("docfx/production/security.md", "both", "will not reuse")]
    [InlineData("docfx/zh-tw/production/security.md", "一起設定", "不會重用")]
    [InlineData("src/WpfDevTools.Inspector.Sdk/README.md", "both", "will not reuse")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs", "both", "will not reuse")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs", "both", "will not reuse")]
    public void Documentation_ShouldDescribeSdkTransportVariablesAsAllOrNothing(string relativePath, string expectedPhrase, string plaintextExpectation)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedPhrase,
            $"{relativePath} should explain that hardened SDK mode requires both transport settings together");
        content.Should().Contain("WPFDEVTOOLS_AUTH_SECRET",
            $"{relativePath} should mention the authentication setting when describing SDK transport coordination");
        content.Should().Contain("WPFDEVTOOLS_CERT_DIR",
            $"{relativePath} should mention the certificate directory setting when describing SDK transport coordination");
        content.Should().Contain(plaintextExpectation,
            $"{relativePath} should explain that the default-hardened MCP server does not reuse plaintext SDK hosts");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    [InlineData("src/WpfDevTools.Inspector.Sdk/README.md")]
    public void Documentation_ShouldDescribeAbsoluteCertificateDirectoryRequirement(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("absolute",
            $"{relativePath} should explain that WPFDEVTOOLS_CERT_DIR must resolve to a shared absolute certificate directory");
        content.Should().Contain("WPFDEVTOOLS_CERT_DIR",
            $"{relativePath} should tie the absolute-path requirement to the supported TLS certificate directory setting");
    }

    private static string ReadDocumentation()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var security = File.ReadAllText(GetRepoFilePath("SECURITY.md"));
        var productionSecurity = File.ReadAllText(GetRepoFilePath("docfx/production/security.md"));
        var zhTwProductionSecurity = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/production/security.md"));
        var docfxIndex = File.ReadAllText(GetRepoFilePath("docfx/index.md"));
        var zhTwDocfxIndex = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/index.md"));
        var architectureOverview = File.ReadAllText(GetRepoFilePath("docfx/architecture/overview.md"));
        var zhTwArchitectureOverview = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/architecture/overview.md"));
        var sdkReadme = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/README.md"));
        return string.Join(
            Environment.NewLine,
            readme,
            security,
            productionSecurity,
            zhTwProductionSecurity,
            docfxIndex,
            zhTwDocfxIndex,
            architectureOverview,
            zhTwArchitectureOverview,
            sdkReadme);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
