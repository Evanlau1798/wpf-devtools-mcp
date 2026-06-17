using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DocfxConfigurationTests
{
    [Fact]
    public void RootToc_ShouldStayShallowBecauseDocfxUsesItForTheTopNavbar()
    {
        var content = ReadRepoText("docfx/toc.yml");

        content.Should().NotContain("items:",
            "the root TOC is rendered by DocFX as the top navbar and deep items can cover article content");
        content.Should().NotContain("api/toc.yml",
            "the generated API TOC is too large for the top navbar and belongs in section navigation");
        content.Should().Contain("href: zh-tw/index.md");
        content.Should().Contain("href: quickstart/index.md");
        content.Should().Contain("href: reference/tools/index.md");
    }

    [Theory]
    [InlineData("docfx/quickstart/toc.yml", "sdk-hosted-inspector.md")]
    [InlineData("docfx/production/toc.yml", "bootstrap-and-injection.md")]
    [InlineData("docfx/reference/toc.yml", "tools/interaction-events-layout.md")]
    [InlineData("docfx/reference/toc.yml", "../api/toc.yml")]
    [InlineData("docfx/architecture/toc.yml", "adrs/adr-006-stdio-session-state.md")]
    [InlineData("docfx/contributors/toc.yml", "../guides/agent-assisted-install.md")]
    public void SectionTocs_ShouldPreserveDeepNavigationOutsideTheTopNavbar(
        string relativePath,
        string expectedHref)
    {
        File.Exists(GetRepoFilePath(relativePath)).Should().BeTrue();
        ReadRepoText(relativePath).Should().Contain(expectedHref);
    }

    [Fact]
    public void MainCss_ShouldConstrainDocfxTablesOnMobile()
    {
        var content = ReadRepoText("docfx/styles/main.css");

        content.Should().Contain("@media (max-width: 767px)");
        content.Should().Contain("article.content table");
        content.Should().Contain("article.content table th");
        content.Should().Contain("overflow-x: auto");
        content.Should().Contain("table-layout: fixed");
        content.Should().Contain("overflow-wrap: anywhere");
        content.Should().Contain("word-break: break-word");
        content.Should().Contain("article.content :not(pre) > code");
        content.Should().Contain("display: inline-block");
        content.Should().Contain("max-width: 100%");
        content.Should().Contain("article.content a");
        content.Should().Contain("article.content table a");
        content.Should().Contain("display: block");
        content.Should().Contain("white-space: normal");
        content.Should().Contain(".anchorjs-link");
        content.Should().Contain("display: none");
    }

    [Fact]
    public void MetadataSources_ShouldReferenceProjectsInsteadOfConfigurationSpecificBuildOutputs()
    {
        var docfx = ReadDocfxConfiguration();
        var sourcePaths = EnumerateMetadataSourcePaths(docfx).ToArray();

        sourcePaths.Should().NotContain(path => path.Contains("/bin/Debug/", StringComparison.OrdinalIgnoreCase));
        sourcePaths.Should().NotContain(path => path.Contains("/bin/Release/", StringComparison.OrdinalIgnoreCase));
        sourcePaths.Should().OnlyContain(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase),
            "DocFX metadata should follow project references instead of stale Debug DLL output paths");
    }

    [Fact]
    public void MetadataSources_ShouldIncludeOnlyPublicApiProjectsAndFilter()
    {
        var docfx = ReadDocfxConfiguration();
        var sourcePaths = EnumerateMetadataSourcePaths(docfx).ToArray();

        sourcePaths.Should().Contain("src/WpfDevTools.Shared/WpfDevTools.Shared.csproj");
        sourcePaths.Should().Contain("src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj");
        sourcePaths.Should().NotContain("src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj");
        docfx.GetProperty("metadata")[0].GetProperty("filter").GetString().Should().Be("filterConfig.yml");
    }

    private static JsonElement ReadDocfxConfiguration()
    {
        var path = GetRepoFilePath("docfx/docfx.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static string ReadRepoText(string relativePath)
        => File.ReadAllText(GetRepoFilePath(relativePath));

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static IEnumerable<string> EnumerateMetadataSourcePaths(JsonElement docfx)
    {
        foreach (var metadataEntry in docfx.GetProperty("metadata").EnumerateArray())
        {
            foreach (var srcEntry in metadataEntry.GetProperty("src").EnumerateArray())
            {
                var srcRoot = srcEntry.GetProperty("src").GetString() ?? string.Empty;
                foreach (var file in srcEntry.GetProperty("files").EnumerateArray())
                {
                    var filePath = file.GetString() ?? string.Empty;
                    yield return NormalizePath(Path.Combine(srcRoot, filePath));
                }
            }
        }
    }

    private static string NormalizePath(string path)
    {
        var combined = Path.GetFullPath(
            Path.Combine(
                GetRepoFilePath("docfx"),
                path));
        var repoRoot = GetRepoFilePath(".");
        return Path.GetRelativePath(repoRoot, combined).Replace('\\', '/');
    }
}
