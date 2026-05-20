using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SdkPreferredDocumentationTests
{
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

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
