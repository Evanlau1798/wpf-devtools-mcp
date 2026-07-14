using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DocfxGeneratedContractSnapshotTests
{
    private static readonly string ToolManifestHash = ComputeJsonSha256(CapabilityResources.GetToolManifest());
    private static readonly string ResponseContractHash = ComputeJsonSha256(CapabilityResources.GetResponseContract());

    [Theory]
    [InlineData("docfx/reference/tools/index.md")]
    [InlineData("docfx/zh-tw/reference/tools/index.md")]
    public void ToolOverviewPages_ShouldPublishCurrentGeneratedContractSnapshot(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("Generated Contract Snapshot");
        content.Should().Contain($"`wpf://contracts/tools` SHA-256: `{ToolManifestHash}`");
        content.Should().Contain($"`wpf://contracts/response` SHA-256: `{ResponseContractHash}`");
    }

    [Theory]
    [InlineData("docfx/reference/tools/index.md")]
    [InlineData("docfx/zh-tw/reference/tools/index.md")]
    public void ToolOverviewPages_ShouldDescribeContractSnapshotValidationScope(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("requiredParameters");
        content.Should().Contain("parameter `constraints`");
        content.Should().Contain("inputSchemaHash");
        content.Should().Contain("outputSchemaHash");
        content.Should().Contain("policyCapabilityTags");
        content.Should().Contain("parameterConstraints");
        content.Should().Contain("parameterVocabularies");
        content.Should().Contain("highValueTools");
    }

    private static string ComputeJsonSha256(string json)
    {
        using var document = JsonDocument.Parse(json);
        var normalized = JsonSerializer.Serialize(document.RootElement);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
