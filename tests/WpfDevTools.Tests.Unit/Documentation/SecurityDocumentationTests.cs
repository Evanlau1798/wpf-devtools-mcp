using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Execution;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SecurityDocumentationTests
{
    private static readonly string[] SecurityOverviewPaths =
    [
        "README.md", "SECURITY.md", "docfx/production/security.md",
        "docfx/zh-tw/production/security.md", "docfx/index.md", "docfx/zh-tw/index.md",
        "docfx/architecture/overview.md", "docfx/zh-tw/architecture/overview.md",
        "src/WpfDevTools.Inspector.Sdk/README.md"
    ];

    private static readonly string[] PolicyPaths =
    [
        "README.md", "SECURITY.md", "docfx/production/security.md",
        "docfx/zh-tw/production/security.md", "docfx/reference/configuration.md",
        "docfx/zh-tw/reference/configuration.md", "src/WpfDevTools.Mcp.Server/ServerInstructions.cs"
    ];

    private static readonly IReadOnlyDictionary<string, string> Documents = LoadDocuments();

    [Fact]
    public void EnvironmentVariableDocumentation_ShouldMatchTheSupportedSecuritySurface()
    {
        var overview = JoinDocuments(SecurityOverviewPaths);
        var supportedVariables = new[]
        {
            "WPFDEVTOOLS_AUTH_SECRET", "WPFDEVTOOLS_CERT_DIR", "WPFDEVTOOLS_CERT_THUMBPRINT",
            "WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS", "WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS",
            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION",
            "WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS"
        };
        var unsupportedVariables = new[]
        {
            "WPFDEVTOOLS_REQUIRE_SIGNATURE", "WPFDEVTOOLS_SKIP_SIGNATURE_CHECK",
            "WPFDEVTOOLS_ENCRYPTION_MODE", "WPFDEVTOOLS_MAX_SESSIONS",
            "WPFDEVTOOLS_RATE_LIMIT", "WPFDEVTOOLS_AUDIT_LOG_PATH"
        };
        var outdatedClaims = new[]
        {
            "By default the server runs without authentication or encryption.",
            "If the variable is not set, authentication is disabled.",
            "Authentication and TLS are opt-in, not automatic.",
            "If the variable is absent, authentication is disabled.",
            "Authentication and TLS are opt-in.",
            "驗證與 TLS 都是 opt-in。"
        };

        using var scope = new AssertionScope();
        foreach (var variable in supportedVariables)
        {
            overview.Should().Contain(variable);
        }

        foreach (var variable in unsupportedVariables)
        {
            Regex.IsMatch(overview, $@"(?<![A-Z0-9_]){Regex.Escape(variable)}(?![A-Z0-9_])")
                .Should().BeFalse($"documentation must not claim unsupported variable {variable}");
        }

        foreach (var claim in outdatedClaims)
        {
            overview.Should().NotContain(claim);
        }

        Read("SECURITY.md").Should().Contain("security-relevant");
        Read("SECURITY.md").Should().NotContain(
            "No other `WPFDEVTOOLS_*` environment variable is currently implemented by the shipping server.");
    }

    [Fact]
    public void PolicyDocumentation_ShouldDefineTrustGatesAndStructuredFailures()
    {
        using var scope = new AssertionScope();
        AssertContainsAll(
            ["README.md", "SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md"],
            "MCP client", "untrusted by default", "server-side policy gates", "redacted");

        AssertMappedPhrase(
            ("README.md", "injection-based"),
            ("SECURITY.md", "injection-based"),
            ("docfx/production/security.md", "Injection-based"),
            ("docfx/zh-tw/production/security.md", "injection"),
            ("docfx/index.md", "persisted local HMAC secret"),
            ("docfx/zh-tw/index.md", "持久化的本機 HMAC secret 與 named-pipe TLS"),
            ("docfx/architecture/overview.md", "shipping injection path hardened by default"),
            ("docfx/zh-tw/architecture/overview.md", "正式發佈的 injection path 預設即為 hardened"));

        AssertContainsAll(
            ["README.md", "SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md", "src/WpfDevTools.Mcp.Server/ServerInstructions.cs"],
            "WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS", "SecurityError", "requiresExplicitTargetOptIn");

        foreach (var path in PolicyPaths)
        {
            DocumentationMarkdown.ExtractVariableContext(Read(path), "WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS")
                .Should().Contain("InvalidPolicyConfiguration", $"{path} must describe malformed raw-injection policy");
            Read(path).Should().NotContain("implicitly trusts only project-scoped targets");
            Read(path).Should().NotContain("outside the default trusted project scope");
            Read(path).Should().NotContain("external raw-injection");
            Read(path).Should().NotContain("外部 raw-injection");
        }

        AssertContainsAll(
            PolicyPaths,
            "WPFDEVTOOLS_MCP_ALLOWED_TARGETS", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
            "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS", "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS",
            "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION",
            "WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS");

        AssertContainsAll(
            [.. PolicyPaths, "docfx/reference/tools/process-and-connection.md", "docfx/zh-tw/reference/tools/process-and-connection.md", "src/WpfDevTools.Mcp.Server/McpTools/ProcessMcpToolDescriptions.cs"],
            "WPFDEVTOOLS_MCP_ALLOWED_TARGETS", "InvalidPolicyConfiguration");
        AssertContainsAll(PolicyPaths, "session state-consuming tools", "capture_state_snapshot", "drain_events");

        foreach (var path in new[] { "SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md" })
        {
            var checklistLines = Read(path).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimStart())
                .Where(line => line.Length > 2 && char.IsDigit(line[0])
                    && (line.Contains("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS", StringComparison.Ordinal)
                        || line.Contains("WPFDEVTOOLS_MCP_ALLOWED_TARGETS", StringComparison.Ordinal)))
                .ToArray();
            checklistLines.Should().HaveCount(2);
            checklistLines.Should().OnlyContain(line => line.Contains("exact local absolute executable path", StringComparison.OrdinalIgnoreCase));
            checklistLines.Should().OnlyContain(line => line.Contains("review", StringComparison.OrdinalIgnoreCase) || line.Contains("審查", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SdkDocumentation_ShouldDescribeCompatibleHardenedTransport()
    {
        var compatibilityPaths = new[]
        {
            "README.md", "SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md",
            "src/WpfDevTools.Inspector.Sdk/README.md", "docfx/guides/troubleshooting.md",
            "docfx/zh-tw/guides/troubleshooting.md", "src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs",
            "src/WpfDevTools.Mcp.Server/ServerInstructions.cs"
        };

        using var scope = new AssertionScope();
        AssertContainsAll(compatibilityPaths, "connect()", "SDK");
        AssertContainsAll(
            ["src/WpfDevTools.Inspector.Sdk/README.md"],
            "can reuse", "matching transport settings", "can still time out", "must set both",
            "InspectorSdk.Initialize()", "will not reuse");

        var allOrNothing = new[]
        {
            ("README.md", "both", "will not reuse"),
            ("SECURITY.md", "both", "will not reuse"),
            ("docfx/production/security.md", "both", "will not reuse"),
            ("docfx/zh-tw/production/security.md", "一起設定", "不會重用"),
            ("src/WpfDevTools.Inspector.Sdk/README.md", "both", "will not reuse"),
            ("src/WpfDevTools.Mcp.Server/ServerInstructions.cs", "both", "will not reuse"),
            ("src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs", "both", "will not reuse")
        };
        foreach (var (path, coordinatedPhrase, plaintextExpectation) in allOrNothing)
        {
            Read(path).Should().Contain(coordinatedPhrase);
            Read(path).Should().Contain("WPFDEVTOOLS_AUTH_SECRET");
            Read(path).Should().Contain("WPFDEVTOOLS_CERT_DIR");
            Read(path).Should().Contain(plaintextExpectation);
        }

        AssertContainsAll(
            ["SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md"],
            "default-pipe");
        AssertMappedTerms(
            ("SECURITY.md", new[] { "DPAPI-protected", "same Windows user" }),
            ("docfx/production/security.md", new[] { "DPAPI-protected", "same Windows user" }),
            ("docfx/zh-tw/production/security.md", new[] { "DPAPI-protected", "同一 Windows 使用者" }));
        AssertMappedTerms(
            ("SECURITY.md", new[] { "CompatibilityError", "before the raw-injection policy denial" }),
            ("docfx/production/security.md", new[] { "CompatibilityError", "before the raw-injection policy denial" }),
            ("docfx/zh-tw/production/security.md", new[] { "CompatibilityError", "先回傳" }));
    }

    [Fact]
    public void CertificateDocumentation_ShouldDescribeStoragePinsAndNegotiationBoundaries()
    {
        using var scope = new AssertionScope();
        AssertContainsAll(
            ["README.md", "SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md", "src/WpfDevTools.Inspector.Sdk/README.md"],
            "absolute", "WPFDEVTOOLS_CERT_DIR");
        AssertContainsAll(
            ["SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md", "docfx/guides/troubleshooting.md", "docfx/zh-tw/guides/troubleshooting.md", "src/WpfDevTools.Inspector.Sdk/InspectorSdkOptions.cs", "src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs"],
            "local absolute", "Network paths are not allowed");

        var productionSecurityPaths = new[] { "SECURITY.md", "docfx/production/security.md", "docfx/zh-tw/production/security.md" };
        AssertContainsAll(productionSecurityPaths, "pins the expected thumbprint", "non-exportable private key", "Exportable");
        foreach (var path in productionSecurityPaths)
        {
            Read(path).Should().NotContain("can pin");
        }

        AssertContainsAll(
            productionSecurityPaths,
            "%APPDATA%\\WpfDevTools\\auth\\shared-secret.bin", "%APPDATA%\\WpfDevTools\\certs", "full-uninstall",
            "Remove-Item -LiteralPath \"$env:APPDATA\\WpfDevTools\\auth\\shared-secret.bin\"",
            "Remove-Item -LiteralPath \"$env:APPDATA\\WpfDevTools\\certs\" -Recurse");
        AssertMappedTerms(
            ("SECURITY.md", new[] { "scripts/tests/Test-TlsNegotiation.ps1", "net8-net8", "TLS 1.3" }),
            ("docfx/production/security.md", new[] { "scripts/tests/Test-TlsNegotiation.ps1", "net8-net48", "TLS 1.3" }),
            ("docfx/zh-tw/production/security.md", new[] { "scripts/tests/Test-TlsNegotiation.ps1", "net48-net8", "TLS 1.3" }));

        Read("src/WpfDevTools.Shared/Security/CertificateManager.cs").Should().NotContain("private key persistable");
        Read("src/WpfDevTools.Shared/Security/CertificateManager.cs").Should().Contain("non-exportable");
    }

    private static void AssertContainsAll(IEnumerable<string> paths, params string[] terms)
    {
        foreach (var path in paths)
        {
            foreach (var term in terms)
            {
                Read(path).Should().Contain(term, $"{path} must contain '{term}'");
            }
        }
    }

    private static void AssertMappedPhrase(params (string Path, string Phrase)[] expectations)
    {
        foreach (var (path, phrase) in expectations)
        {
            Read(path).Should().Contain(phrase);
        }
    }

    private static void AssertMappedTerms(params (string Path, string[] Terms)[] expectations)
    {
        foreach (var (path, terms) in expectations)
        {
            AssertContainsAll([path], terms);
        }
    }

    private static string Read(string relativePath) => Documents[relativePath];

    private static string JoinDocuments(IEnumerable<string> paths)
        => string.Join(Environment.NewLine, paths.Select(Read));

    private static IReadOnlyDictionary<string, string> LoadDocuments()
    {
        var paths = SecurityOverviewPaths
            .Concat(PolicyPaths)
            .Concat(
            [
                "docfx/reference/tools/process-and-connection.md", "docfx/zh-tw/reference/tools/process-and-connection.md",
                "src/WpfDevTools.Mcp.Server/McpTools/ProcessMcpToolDescriptions.cs",
                "docfx/guides/troubleshooting.md", "docfx/zh-tw/guides/troubleshooting.md",
                "src/WpfDevTools.Mcp.Server/McpResources/CapabilityResources.cs",
                "src/WpfDevTools.Inspector.Sdk/InspectorSdkOptions.cs",
                "src/WpfDevTools.Shared/Security/CertificateManager.cs"
            ])
            .Distinct(StringComparer.Ordinal);
        return paths.ToDictionary(
            path => path,
            path => File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path)),
            StringComparer.Ordinal);
    }
}
