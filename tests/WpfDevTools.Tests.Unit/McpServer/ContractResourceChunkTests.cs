using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ContractResourceChunkTests
{
    private const int MaxChunkBytes = 16 * 1024;

    public static readonly TheoryData<string> ContractIds = new()
    {
        "response",
        "tools",
        "tool-examples"
    };

    [Fact]
    public void ContractIndex_ShouldPublishExactReconstructionMetadata()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetContractResourceIndex());
        var root = document.RootElement;
        var resources = root.GetProperty("resources").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("id").GetString()!, StringComparer.Ordinal);

        root.GetProperty("resourceUri").GetString().Should().Be("wpf://contracts/index");
        root.GetProperty("maxChunkBytes").GetInt32().Should().Be(MaxChunkBytes);
        resources.Keys.Should().BeEquivalentTo(["response", "tools", "tool-examples"]);

        foreach (var (id, content) in ExpectedContracts())
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var entry = resources[id];
            entry.GetProperty("byteLength").GetInt32().Should().Be(bytes.Length);
            entry.GetProperty("sha256").GetString().Should().Be(Sha256(bytes));
            entry.GetProperty("chunkUriTemplate").GetString()
                .Should().Be($"wpf://contracts/{id}/chunks/{{offset}}/{{length}}");
        }
    }

    [Fact]
    public void Discovery_ShouldRouteTruncatedContractReadsToVerifiedChunks()
    {
        CapabilityResources.GetCapabilities().Should().Contain("wpf://contracts/index")
            .And.Contain("16 KiB")
            .And.Contain("SHA-256");
        ServerInstructions.Value.Should().Contain("wpf://contracts/index")
            .And.Contain("16 KiB")
            .And.Contain("SHA-256");
    }

    [Theory]
    [MemberData(nameof(ContractIds))]
    public void ContractChunks_ShouldReconstructTheExactUtf8Resource(string contractId)
    {
        var expected = Encoding.UTF8.GetBytes(ExpectedContracts()[contractId]);
        using var reconstructed = new MemoryStream();

        for (var offset = 0; offset < expected.Length; offset += MaxChunkBytes)
        {
            var blob = CapabilityResources.GetContractResourceChunk(
                    contractId,
                    offset,
                    MaxChunkBytes)
                .Should().BeOfType<BlobResourceContents>().Subject;
            blob.Uri.Should().Be($"wpf://contracts/{contractId}/chunks/{offset}/{MaxChunkBytes}");
            blob.MimeType.Should().Be("application/octet-stream");
            reconstructed.Write(blob.DecodedData.Span);
        }

        reconstructed.ToArray().Should().Equal(expected);
        Sha256(reconstructed.ToArray()).Should().Be(Sha256(expected));
    }

    [Fact]
    public void ContractChunk_WithUnknownId_ShouldReturnResourceNotFound()
    {
        var act = () => CapabilityResources.GetContractResourceChunk("unknown", 0, 1);

        act.Should().Throw<McpProtocolException>()
            .Where(exception => exception.ErrorCode == McpErrorCode.ResourceNotFound);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, MaxChunkBytes + 1)]
    [InlineData(int.MaxValue, 1)]
    public void ContractChunk_WithInvalidRange_ShouldReject(int offset, int length)
    {
        var act = () => CapabilityResources.GetContractResourceChunk("response", offset, length);

        act.Should().Throw<McpProtocolException>()
            .Where(exception => exception.ErrorCode == McpErrorCode.InvalidParams);
    }

    private static IReadOnlyDictionary<string, string> ExpectedContracts()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response"] = CapabilityResources.GetResponseContract(),
            ["tools"] = CapabilityResources.GetToolManifest(),
            ["tool-examples"] = CapabilityResources.GetToolExamples()
        };

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
