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
    private const int MaxTextChunkBytes = 8 * 1024;

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
        root.GetProperty("maxTextChunkBytes").GetInt32().Should().Be(MaxTextChunkBytes);
        resources.Keys.Should().BeEquivalentTo(["response", "tools", "tool-examples"]);

        foreach (var (id, content) in ExpectedContracts())
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var entry = resources[id];
            entry.GetProperty("byteLength").GetInt32().Should().Be(bytes.Length);
            entry.GetProperty("sha256").GetString().Should().Be(Sha256(bytes));
            entry.GetProperty("chunkUriTemplate").GetString()
                .Should().Be($"wpf://contracts/{id}/chunks/{{offset}}/{{length}}");
            entry.GetProperty("textChunkUriTemplate").GetString()
                .Should().Be($"wpf://contracts/{id}/text-chunks/{{offset}}/{{length}}");
        }
    }

    [Fact]
    public void Discovery_ShouldRouteTruncatedContractReadsToVerifiedChunks()
    {
        CapabilityResources.GetCapabilities().Should().Contain("wpf://contracts/index")
            .And.Contain("16 KiB")
            .And.Contain("text-chunks")
            .And.Contain("base64")
            .And.Contain("SHA-256");
        ServerInstructions.Value.Should().Contain("wpf://contracts/index")
            .And.Contain("16 KiB")
            .And.Contain("text-chunks")
            .And.Contain("base64")
            .And.Contain("SHA-256");
    }

    [Theory]
    [MemberData(nameof(ContractIds))]
    public void ContractTextChunks_ShouldReconstructWithoutClientBinaryDecoding(string contractId)
    {
        var expected = ExpectedContracts()[contractId];
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var reconstructed = new StringBuilder();
        var offset = 0;

        while (offset < expectedBytes.Length)
        {
            var envelopeJson = CapabilityResources.GetContractResourceTextChunk(
                contractId,
                offset,
                MaxTextChunkBytes);
            using var envelope = JsonDocument.Parse(envelopeJson);
            var root = envelope.RootElement;
            root.GetProperty("offset").GetInt32().Should().Be(offset);
            root.GetProperty("resourceByteLength").GetInt32().Should().Be(expectedBytes.Length);
            root.GetProperty("resourceSha256").GetString().Should().Be(Sha256(expectedBytes));
            reconstructed.Append(root.GetProperty("text").GetString());
            var nextOffset = root.GetProperty("nextOffset").GetInt32();
            nextOffset.Should().BeGreaterThan(offset);
            offset = nextOffset;
        }

        reconstructed.ToString().Should().Be(expected);
        offset.Should().Be(expectedBytes.Length);
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

    [Fact]
    public void ContractTextChunk_WithUnknownId_ShouldReturnResourceNotFound()
    {
        var act = () => CapabilityResources.GetContractResourceTextChunk("unknown", 0, 1);

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

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, MaxTextChunkBytes + 1)]
    [InlineData(int.MaxValue, 1)]
    public void ContractTextChunk_WithInvalidRange_ShouldReject(int offset, int length)
    {
        var act = () => CapabilityResources.GetContractResourceTextChunk("response", offset, length);

        act.Should().Throw<McpProtocolException>()
            .Where(exception => exception.ErrorCode == McpErrorCode.InvalidParams);
    }

    [Fact]
    public void ContractTextChunk_WithMultibyteUtf8_ShouldShrinkToCharacterBoundaries()
    {
        var bytes = Encoding.UTF8.GetBytes("A€B");

        CapabilityResources.GetContractUtf8TextSlice(bytes, 0, 2)
            .Should().Be(("A", 1));
        CapabilityResources.GetContractUtf8TextSlice(bytes, 1, 3)
            .Should().Be(("€", 4));
        CapabilityResources.GetContractUtf8TextSlice(bytes, 4, 1)
            .Should().Be(("B", 5));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void ContractTextChunk_WithInvalidMultibyteBoundary_ShouldReject(int offset, int length)
    {
        var bytes = Encoding.UTF8.GetBytes("A€B");
        var act = () => CapabilityResources.GetContractUtf8TextSlice(bytes, offset, length);

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
