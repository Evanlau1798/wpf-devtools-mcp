using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using WpfDevTools.Shared.Serialization;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ReadmeDocumentationTests
{
    private const string StableLatestInstallerCommand =
        "irm https://installer.wpf-mcptools.evanlau1798.com | iex";
    private const string LatestPrereleaseInstallerCommand =
        "& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease";

    [Fact]
    public void Readme_ShouldStayConciseProductionEntrypoint()
    {
        var lines = File.ReadAllLines(GetRepoFilePath("README.md"));

        lines.Length.Should().BeInRange(35, 90);
    }

    [Fact]
    public void Readme_ShouldDelegateDetailedContractsToDocfxSite()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/quickstart/");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/quickstart/ai-agent-clients.html");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/quickstart/sdk-hosted-inspector.html");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/production/deployment.html");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/production/security.html");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/production/release-layout.html");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/contributors/");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/contributors/public-path-runtime-security.html");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com/reference/tools/");

        content.Should().NotContain("docfx/index.md");
        content.Should().NotContain("docfx/quickstart/index.md");
        content.Should().NotContain("## MCP Client Configuration");
        content.Should().NotContain("## Tool Categories");
        content.Should().NotContain("```json");
    }

    [Fact]
    public void Readme_ShouldDescribeCurrentPublicSurfaceAndSecurityDefaults()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("STDIO");
        content.Should().Contain("Security defaults fail closed");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS");
        content.Should().Contain("WPFDEVTOOLS_AUTH_SECRET");
        content.Should().Contain("WPFDEVTOOLS_CERT_DIR");
        content.Should().NotContain("HTTP+SSE currently available");
    }

    [Fact]
    public void Readme_ShouldAdvertiseCurrentMcpSdkVersion()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("MCP-SDK%20v1.4");
        content.Should().NotContain("MCP-SDK%20v1.0");
    }

    [Fact]
    public void Readme_ShouldDescribePublishedInstallArtifactsWithoutE2eRunbookResidue()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain(StableLatestInstallerCommand);
        content.Should().NotContain(LatestPrereleaseInstallerCommand);
        content.Should().Contain("pre-release");
        content.Should().Contain("release_<version>_win-<arch>.zip");
        content.Should().Contain("run.bat");
        content.Should().Contain("trusted release metadata");
        content.Should().Contain("before using package-local `run.bat`");
        content.Should().Contain("Quickstart");
        content.Should().Contain("Deployment Guide");
        content.Should().Contain("Release Layout");
        content.Should().NotContain("SHA256SUMS.txt");
        content.Should().NotContain("release-assets.json");
        content.Should().NotContain("release-sbom.spdx.json");
        content.Should().NotContain("package-sbom.spdx.json");
        content.Should().NotContain("release-evidence.json");
        content.Should().NotContain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        content.Should().NotContain("GitHub pre-release E2E");
        content.Should().NotContain("validation-only");
        content.Should().NotContain("NDJSON smoke");
        content.Should().NotContain("stable release assets and anonymous endpoint smoke checks have passed");
        content.Should().NotContain("-ExecutionPolicy Bypass");
    }

    [Fact]
    public void Readme_ShouldShowConcretePinnedPrereleaseInstallExample()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("$version = 'v1.0.0-beta.66'");
        content.Should().Contain("-Version $version -Prerelease");
        content.Should().Contain("Pinned pre-release install");
        content.Should().NotContain("$version = '<version>'");
        content.Should().NotContain("0.1.0-e2e.");
    }

    [Fact]
    public void Readme_ShouldAvoidRawJsonRpcTutorials()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().NotContain("tools/call");
        content.Should().NotContain("\"jsonrpc\"");
    }

    [Fact]
    public async Task Readme_ShouldMatchInspectorNamedPipeFramingContract()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/architecture/overview.md"));
        var architectureSection = content.Split("## Data flow", 2)[1].Split("## Main components", 2)[0];
        var inspectorIpcSentence = architectureSection
            .Split('\n')
            .Single(line => line.Contains("Named Pipes") && line.Contains("length-prefixed"));
        var requestSource = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Shared/Messages/InspectorRequest.cs"));
        var responseSource = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Shared/Messages/InspectorResponse.cs"));
        const string jsonPayload = "{\"id\":\"readme-contract\",\"method\":\"ping\",\"params\":{}}";
        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        using var stream = new MemoryStream();
        await MessageFraming.WriteMessageAsync(stream, jsonPayload);
        var frame = stream.ToArray();

        frame.Should().HaveCount(4 + payloadBytes.Length);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(payloadBytes.Length);
        frame[4..].Should().Equal(payloadBytes);
        requestSource.Should().NotContain("\"jsonrpc\"");
        responseSource.Should().NotContain("\"jsonrpc\"");
        inspectorIpcSentence.Should().Contain("custom length-prefixed JSON request/response messages");
        inspectorIpcSentence.Should().Contain("4-byte little-endian length");
        inspectorIpcSentence.Should().NotContain("JSON-RPC");
    }

    [Fact]
    public void Readme_ShouldReferenceOnlyCommittedRepositoryDocs()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("LICENSE");
        content.Should().Contain("RELEASING.md");
        content.Should().Contain("AGENT_INSTALL.md");
        content.Should().NotContain("docs/current-state.md");
        content.Should().NotContain("docs/mcp-sdk-plan/README.md");
    }

    [Fact]
    public void DocfxArchitecture_ShouldListBootstrapperAndInspectorSdkProjects()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/architecture/overview.md"));

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
    public void DocfxToolReference_ShouldDocumentElementScreenshotOutputModes()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/reference/tools/interaction-events-layout.md"));

        content.Should().Contain("element_screenshot");
        content.Should().Contain("outputMode");
        content.Should().Contain("metadata");
        content.Should().Contain("file");
        content.Should().Contain("base64");
        content.Should().Contain("screenshotId");
        content.Should().Contain("resourceUri");
        content.Should().Contain("sha256");
        content.Should().NotContain("file mode also returns `screenshotId`, `path`, and `sha256`");
        content.Should().NotContain("`element_screenshot` returns `base64Image`, `width`, `height`, and `format`.");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
