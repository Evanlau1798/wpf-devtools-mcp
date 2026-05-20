using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SdkPreferredDocumentationTests
{
    private static readonly string[] SdkUsageFiles =
    [
        "docfx/quickstart/sdk-hosted-inspector.md",
        "docfx/zh-tw/quickstart/sdk-hosted-inspector.md",
        "src/WpfDevTools.Inspector.Sdk/README.md"
    ];

    [Fact]
    public void SdkHostedQuickstart_ShouldExistAndBeLinkedFromPublicEntrypoints()
    {
        File.Exists(GetRepoFilePath("docfx/quickstart/sdk-hosted-inspector.md")).Should().BeTrue();
        File.Exists(GetRepoFilePath("docfx/zh-tw/quickstart/sdk-hosted-inspector.md")).Should().BeTrue();

        File.ReadAllText(GetRepoFilePath("README.md"))
            .Should().Contain("docfx/quickstart/sdk-hosted-inspector.md");
        File.ReadAllText(GetRepoFilePath("docfx/toc.yml"))
            .Should().Contain("quickstart/sdk-hosted-inspector.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/toc.yml"))
            .Should().Contain("quickstart/sdk-hosted-inspector.md");
        File.ReadAllText(GetRepoFilePath("docfx/quickstart/index.md"))
            .Should().Contain("sdk-hosted-inspector.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/index.md"))
            .Should().Contain("sdk-hosted-inspector.md");
    }

    [Fact]
    public void SdkHostedQuickstart_ShouldDocumentRequiredWorkflowAndBoundaries()
    {
        foreach (var file in new[]
                 {
                     "docfx/quickstart/sdk-hosted-inspector.md",
                     "docfx/zh-tw/quickstart/sdk-hosted-inspector.md"
                 })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("WpfDevTools.Inspector.Sdk");
            content.Should().Contain("InspectorSdkOptions");
            content.Should().Contain("AuthenticationSecretBase64");
            content.Should().Contain("CertificateDirectory");
            content.Should().Contain("InspectorSdk.Initialize()");
            content.Should().Contain("InspectorSdk.Shutdown()");
            content.Should().Contain("WPFDEVTOOLS_AUTH_SECRET");
            content.Should().Contain("WPFDEVTOOLS_CERT_DIR");
            content.Should().Contain("absolute");
            content.Should().Contain("connect()");
            content.Should().Contain("SDK-hosted");
            content.Should().Contain("raw injection");
            content.Should().Contain("fallback");
            content.Should().Contain("zero-instrumentation");
            content.Should().Contain("single-file");
            content.Should().Contain("Native AOT");
            content.Should().Contain("trimmed");
            content.Should().Contain("AV");
            content.Should().Contain("deployment policy");
            content.Should().Contain("net8.0-windows");
            content.Should().Contain("NuGet package is not yet publicly published");
            content.Should().Contain("local pack");
        }
    }

    [Fact]
    public void SdkUsageDocs_ShouldShowExplicitOptionsExampleAndBoundaries()
    {
        foreach (var file in SdkUsageFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("InspectorSdk.Initialize(new InspectorSdkOptions");
            content.Should().Contain("ProcessId = Environment.ProcessId");
            content.Should().Contain("AuthenticationSecretBase64 = authSecretBase64");
            content.Should().Contain("CertificateDirectory = certificateDirectory");
            content.Should().Contain("Partial explicit SDK transport configuration is rejected");
            content.Should().Contain("not mixed with environment variables");
            content.Should().Contain("The MCP server must use the same secret and certificate directory");
        }
    }

    [Fact]
    public void SdkPreferredPositioning_ShouldStayConsistentAcrossPublicDocs()
    {
        foreach (var file in new[]
                 {
                     "README.md",
                     "docfx/architecture/overview.md",
                     "docfx/production/compatibility-matrix.md",
                     "docfx/production/bootstrap-and-injection.md",
                     "src/WpfDevTools.Inspector.Sdk/README.md"
                 })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("prefer SDK-hosted", $"{file} should describe the target-app-owned path as SDK-preferred");
            content.Should().Contain("raw injection", $"{file} should keep the fallback path explicit");
            content.Should().Contain("fallback", $"{file} should avoid implying raw injection is removed");
        }

        foreach (var file in new[]
                 {
                     "docfx/zh-tw/architecture/overview.md",
                     "docfx/zh-tw/production/compatibility-matrix.md",
                     "docfx/zh-tw/production/bootstrap-and-injection.md"
                 })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("prefer SDK-hosted", $"{file} should keep the SDK-preferred positioning searchable");
            content.Should().Contain("raw injection");
            content.Should().Contain("fallback");
        }
    }

    [Fact]
    public void SdkProject_ShouldStayNet8WindowsOnlyUntilTargetExpansionIsImplemented()
    {
        var project = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj"));
        project.Should().Contain("<TargetFramework>net8.0-windows</TargetFramework>");
        project.Should().NotContain("<TargetFrameworks>");

        File.ReadAllText(GetRepoFilePath("docfx/quickstart/sdk-hosted-inspector.md"))
            .Should().Contain(".NET Framework WPF apps should keep using the raw injection path");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/sdk-hosted-inspector.md"))
            .Should().Contain(".NET Framework WPF app 應維持使用 raw injection path");
    }

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
