using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ReadmeDocumentationTests
{
    [Fact]
    public void Readme_ShouldStayUnderFiveHundredLines()
    {
        var lines = File.ReadAllLines(GetRepoFilePath("README.md"));

        lines.Length.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public void Readme_ShouldDescribeCurrentStdioOnlyState()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("STDIO");
        content.Should().Contain("WithStdioServerTransport");
        content.Should().NotContain("HTTP+SSE currently available");
    }

    [Fact]
    public void Readme_ShouldAvoidRawJsonRpcTutorials()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().NotContain("tools/call");
        content.Should().NotContain("\"jsonrpc\"");
    }

    [Fact]
    public void Readme_ShouldReferenceOnlyCommittedRepositoryDocs()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("SECURITY.md");
        content.Should().Contain("EXAMPLES.md");
        content.Should().NotContain("docs/current-state.md");
        content.Should().NotContain("docs/mcp-sdk-plan/README.md");
        content.Should().NotContain("docs/development-plan/README.md");
        content.Should().NotContain("docs/architecture/");
    }

    [Fact]
    public void Readme_ShouldDocumentStructuredInspectorErrors()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("errorCode");
        content.Should().Contain("errorData");
    }

    [Fact]
    public void Readme_ShouldListBootstrapperAndInspectorSdkProjectsInRepositoryLayout()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("WpfDevTools.Bootstrapper/");
        content.Should().Contain("WpfDevTools.Inspector.Sdk/");
    }

    [Fact]
    public void Program_ShouldUseEmptyApplicationBuilder_ForStdioServer()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/Program.cs"));

        content.Should().Contain("Host.CreateEmptyApplicationBuilder(");
    }

    [Fact]
    public void Readme_ShouldDocumentElementScreenshotOutputModes()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("element_screenshot");
        content.Should().Contain("outputMode");
        content.Should().Contain("metadata");
        content.Should().Contain("file");
        content.Should().Contain("base64");
        content.Should().Contain("screenshotId");
        content.Should().Contain("sha256");
        content.Should().NotContain("`element_screenshot` returns `base64Image`, `width`, `height`, and `format`.");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
    [Fact]
    public void Readme_ShouldDocumentPublishedArtifactSetupAndServerBitness()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("published release",
            "the public onboarding path should mention published artifacts instead of only source-tree startup");
        content.Should().Contain("server process architecture must match the target process",
            "README quick start must state that the MCP server/injector bitness must match the target app");
    }

    [Fact]
    public void Readme_ShouldDescribeConnectFirstSceneFirstWorkflow()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("connect()",
            "the primary workflow should start from connect() auto-discovery");
        content.Should().Contain("get_ui_summary",
            "README should direct agents toward scene-level context before tree expansion");
        content.Should().Contain("nextSteps",
            "README should explain that clients should prefer returned runtime navigation guidance");
        content.Should().Contain("get_processes(windowFilter)",
            "README should frame process listing as a disambiguation path rather than the default path");
        content.Should().NotContain("Call `get_processes` to discover WPF targets.",
            "README should not instruct agents to list processes before trying connect()");
        content.Should().NotContain("Use tree tools first",
            "README should not present tree-first exploration as the default path");
    }

    [Fact]
    public void Readme_ShouldDescribeConditionalRunBatElevationBehavior()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("WPFDEVTOOLS_SKIP_ELEVATION=1",
            "README should explain the opt-out for the launcher elevation request");
        content.Should().Contain("requests elevation when the current shell is not already elevated",
            "README should describe the current conditional elevation behavior precisely");
        content.Should().Contain("register manually after install",
            "README should document the manual fallback when elevated CLI registration is blocked");
        content.Should().NotContain("WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH",
            "README must not document the removed elevated CLI path opt-in");
    }

    [Fact]
    public void Readme_ShouldUseAutomationSafeInstallerExamplesAndLiteralAbsoluteJsonPaths()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("-NonInteractive",
            "README should not describe an automation-ready install flow without explicitly passing -NonInteractive");
        content.Should().Contain("-OutputJson",
            "README should include machine-readable installer output in automation-oriented examples");
        content.Should().NotContain("%APPDATA%\\\\WpfDevToolsMcp\\\\x64\\\\current\\\\bin\\\\wpf-devtools-x64.exe",
            "published JSON examples should use literal absolute paths rather than default-root placeholders");
        content.Should().Contain("C:\\\\Users\\\\<you>\\\\AppData\\\\Roaming\\\\WpfDevToolsMcp\\\\<arch>\\\\current\\\\bin\\\\wpf-devtools-<arch>.exe",
            "README should demonstrate the reviewed JSON shape with a literal absolute path template");
    }

    [Fact]
    public void Readme_ShouldUseReviewedOnlineInstallerAsPrimaryBootstrapPath()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().NotContain("raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1",
            "README should not ask users to execute the moving master branch installer directly");
        content.Should().Contain(".\\scripts\\online-installer.ps1",
            "README should make the reviewed repository installer the primary bootstrap path");
        content.Should().Contain("validates archive integrity before extraction",
            "README should explain why the reviewed installer path is safer than manually expanding the archive");
        content.Should().Contain("run.bat",
            "README should keep the package-local launcher as the manual fallback path");
        content.Should().Contain("SHA256SUMS.txt",
            "README manual fallback should require release provenance verification before run.bat is launched from an extracted archive");
        content.Should().Contain("release-assets.json",
            "README manual fallback should point users at the canonical release asset metadata before trusting a downloaded archive");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
            "README manual fallback should explain how to provide an explicit signer pin if the verified archive is no longer kept beside the extracted package");
    }

    [Fact]
    public void LandingPages_ShouldDescribeSignerFallbackAsEitherPinnedSignerValue()
    {
        var englishLandingPages = new[]
        {
            "README.md",
            "docfx/index.md"
        };

        foreach (var relativePath in englishLandingPages)
        {
            var content = File.ReadAllText(GetRepoFilePath(relativePath));

            content.Should().Contain("`WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`");
            content.Should().NotContain("`WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` and `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`");
        }

        var traditionalChinese = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/index.md"));
        traditionalChinese.Should().Contain("`WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 或 `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`");
        traditionalChinese.Should().NotContain("`WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 與 `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`");
    }
}
