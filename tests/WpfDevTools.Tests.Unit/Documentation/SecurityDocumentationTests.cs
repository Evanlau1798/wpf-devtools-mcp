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
    [InlineData("WPFDEVTOOLS_MCP_ALLOWED_TARGETS")]
    [InlineData("WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS")]
    [InlineData("WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS")]
    [InlineData("WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION")]
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

    [Fact]
    public void SecurityDocumentation_ShouldDescribeItsEnvironmentVariableTableAsSecurityScoped()
    {
        var content = File.ReadAllText(GetRepoFilePath("SECURITY.md"));

        content.Should().Contain("security-relevant",
            "SECURITY.md should describe its environment-variable table as security-scoped instead of claiming it is the complete server configuration surface");
        content.Should().NotContain("No other `WPFDEVTOOLS_*` environment variable is currently implemented by the shipping server.",
            "SECURITY.md should not contradict non-security configuration knobs such as WPFDEVTOOLS_RATE_LIMIT_RPM");
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
    [InlineData("docfx/reference/configuration.md")]
    [InlineData("docfx/zh-tw/reference/configuration.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs")]
    public void Documentation_ShouldNotDescribeRepositoryTargetsAsImplicitRawInjectionTrust(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain("implicitly trusts only project-scoped targets",
            $"{relativePath} should not document repository-root raw injection trust as a default");
        content.Should().NotContain("outside the default trusted project scope",
            $"{relativePath} should describe exact allowlist behavior instead of default project trust");
        content.Should().NotContain("external raw-injection",
            $"{relativePath} should not imply the raw-injection allowlist only applies to external targets");
        content.Should().NotContain("外部 raw-injection",
            $"{relativePath} should not imply the raw-injection allowlist only applies to external targets");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    [InlineData("docfx/reference/configuration.md")]
    [InlineData("docfx/zh-tw/reference/configuration.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs")]
    public void Documentation_ShouldDescribeMcpPolicyGates(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            $"{relativePath} should document the connect target allowlist gate");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
            $"{relativePath} should document the destructive tool gate");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS",
            $"{relativePath} should document the screenshot gate");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION",
            $"{relativePath} should document the ViewModel inspection gate");
    }

    [Theory]
    [InlineData("README.md", "session state-consuming tools")]
    [InlineData("SECURITY.md", "session state-consuming tools")]
    [InlineData("docfx/production/security.md", "session state-consuming tools")]
    [InlineData("docfx/reference/configuration.md", "session state-consuming tools")]
    [InlineData("docfx/zh-tw/production/security.md", "session state-consuming tools")]
    [InlineData("docfx/zh-tw/reference/configuration.md", "session state-consuming tools")]
    [InlineData("src/WpfDevTools.Mcp.Server/ServerInstructions.cs", "session state-consuming tools")]
    public void Documentation_ShouldDescribeDestructiveGateForSessionStateConsumingTools(
        string relativePath,
        string expectedCategory)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedCategory,
            $"{relativePath} should explain that the destructive policy gate also covers tools that consume or mutate MCP session state");
        content.Should().Contain("capture_state_snapshot",
            $"{relativePath} should name the destructive snapshot capture gate coverage");
        content.Should().Contain("drain_events",
            $"{relativePath} should name the destructive buffered-event drain gate coverage");
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
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void Documentation_ShouldDescribeDefaultPipeHostCompatibilityValidation(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("default-pipe",
            $"{relativePath} should describe the general default-pipe host validation guard, not only SDK-host reuse");
    }

    [Theory]
    [InlineData("SECURITY.md", "DPAPI-protected", "same Windows user")]
    [InlineData("docfx/production/security.md", "DPAPI-protected", "same Windows user")]
    [InlineData("docfx/zh-tw/production/security.md", "DPAPI-protected", "同一 Windows 使用者")]
    public void Documentation_ShouldDescribeBootstrapSecretHandoffBoundary(
        string relativePath,
        string protectionPhrase,
        string boundaryPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(protectionPhrase,
            $"{relativePath} should describe that injection bootstrap auth-secret handoff files are locally protected");
        content.Should().Contain(boundaryPhrase,
            $"{relativePath} should state that same-user local code is still inside the trust boundary");
    }

    [Theory]
    [InlineData("SECURITY.md", "CompatibilityError", "before the raw-injection policy denial")]
    [InlineData("docfx/production/security.md", "CompatibilityError", "before the raw-injection policy denial")]
    [InlineData("docfx/zh-tw/production/security.md", "CompatibilityError", "先回傳")]
    public void Documentation_ShouldDescribeCompatibilityErrorPrecedenceForBlockedExternalTargets(string relativePath, string errorCode, string precedencePhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(errorCode,
            $"{relativePath} should document the stale-host compatibility error surface for blocked external targets");
        content.Should().Contain(precedencePhrase,
            $"{relativePath} should explain that compatibility rejection can surface before the raw injection policy denial");
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

    [Theory]
    [InlineData("SECURITY.md", "local absolute", "Network paths are not allowed")]
    [InlineData("docfx/production/security.md", "local absolute", "Network paths are not allowed")]
    [InlineData("docfx/zh-tw/production/security.md", "local absolute", "Network paths are not allowed")]
    [InlineData("docfx/guides/troubleshooting.md", "local absolute", "Network paths are not allowed")]
    [InlineData("docfx/zh-tw/guides/troubleshooting.md", "local absolute", "Network paths are not allowed")]
    [InlineData("src/WpfDevTools.Inspector.Sdk/InspectorSdkOptions.cs", "local absolute", "Network paths are not allowed")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs", "local absolute", "Network paths are not allowed")]
    public void Documentation_ShouldDescribeLocalCertificateDirectoryRequirement(
        string relativePath,
        string localRequirement,
        string networkPathWarning)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(localRequirement,
            $"{relativePath} should explain that WPFDEVTOOLS_CERT_DIR must be a local absolute directory");
        content.Should().Contain(networkPathWarning,
            $"{relativePath} should explain that network or UNC certificate directories are rejected");
    }

    [Theory]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void Documentation_ShouldDescribeTlsThumbprintPinAsRequiredWhenSubjectIsValidated(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("pins the expected thumbprint",
            $"{relativePath} should describe thumbprint pinning as part of TLS certificate validation");
        content.Should().NotContain("can pin",
            $"{relativePath} should not imply subject-only TLS certificate validation is acceptable");
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
