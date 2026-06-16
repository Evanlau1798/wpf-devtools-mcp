using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DocfxConfigurationTests
{
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
        var path = WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath("docfx/docfx.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

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
                WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath("docfx"),
                path));
        var repoRoot = WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(".");
        return Path.GetRelativePath(repoRoot, combined).Replace('\\', '/');
    }
}
