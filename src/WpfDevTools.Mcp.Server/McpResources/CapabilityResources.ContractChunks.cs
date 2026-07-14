using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpResources;

public static partial class CapabilityResources
{
    internal const int MaxContractChunkBytes = 16 * 1024;
    internal const int MaxContractTextChunkBytes = 8 * 1024;
    private const string ContractIndexResourceUri = "wpf://contracts/index";
    private static readonly Lazy<IReadOnlyDictionary<string, ContractResourceSnapshot>> ContractSnapshots =
        new(CreateContractSnapshots, LazyThreadSafetyMode.ExecutionAndPublication);

    [McpServerResource(
        Name = "wpf_contract_index",
        Title = "Contract Resource Index",
        UriTemplate = ContractIndexResourceUri,
        MimeType = "application/json")]
    [Description("Compact byte-length, SHA-256, and chunk-URI metadata for reconstructing large WPF contract resources across bounded MCP clients.")]
    public static string GetContractResourceIndex()
    {
        var resources = ContractSnapshots.Value.Values
            .OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => new
            {
                id = resource.Id,
                resourceUri = resource.ResourceUri,
                mimeType = "application/json",
                byteLength = resource.Bytes.Length,
                sha256 = resource.Sha256,
                chunkUriTemplate = $"wpf://contracts/{resource.Id}/chunks/{{offset}}/{{length}}",
                textChunkUriTemplate = $"wpf://contracts/{resource.Id}/text-chunks/{{offset}}/{{length}}"
            });
        return JsonSerializer.Serialize(new
        {
            resourceUri = ContractIndexResourceUri,
            version = "1.1",
            maxChunkBytes = MaxContractChunkBytes,
            maxTextChunkBytes = MaxContractTextChunkBytes,
            encoding = "utf-8",
            chunkMimeType = "application/octet-stream",
            textChunkMimeType = "application/json",
            reconstruction = "Read sequential chunks from offset 0, concatenate decoded bytes without transformation, then verify byteLength and sha256 before parsing JSON.",
            textChunkReconstruction = "Read sequential text-chunk envelopes from offset 0, append each text field, advance to nextOffset, then parse JSON. No base64 or client-side hashing is required; every envelope includes the canonical snapshot SHA-256.",
            resources
        }, JsonResourceSerializerOptions);
    }

    [McpServerResource(
        Name = "wpf_contract_chunk",
        Title = "Contract Resource Chunk",
        UriTemplate = "wpf://contracts/{contractId}/chunks/{offset}/{length}",
        MimeType = "application/octet-stream")]
    [Description("Reads at most 16 KiB from a canonical UTF-8 contract snapshot for clients whose bridge truncates the complete JSON resource.")]
    public static ResourceContents GetContractResourceChunk(
        string contractId,
        int offset,
        int length)
    {
        if (!ContractSnapshots.Value.TryGetValue(contractId, out var resource))
        {
            throw new McpProtocolException(
                $"Contract resource '{contractId}' is not available. Read {ContractIndexResourceUri} for valid IDs.",
                McpErrorCode.ResourceNotFound);
        }

        if (offset < 0
            || offset >= resource.Bytes.Length
            || length <= 0
            || length > MaxContractChunkBytes)
        {
            throw new McpProtocolException(
                $"Contract chunk requires offset within the {resource.Bytes.Length}-byte resource and length from 1 to {MaxContractChunkBytes} bytes.",
                McpErrorCode.InvalidParams);
        }

        var count = Math.Min(length, resource.Bytes.Length - offset);
        var bytes = resource.Bytes.AsSpan(offset, count).ToArray();
        var uri = $"wpf://contracts/{contractId}/chunks/{offset}/{length}";
        return BlobResourceContents.FromBytes(bytes, uri, "application/octet-stream");
    }

    [McpServerResource(
        Name = "wpf_contract_text_chunk",
        Title = "Contract Resource Text Chunk",
        UriTemplate = "wpf://contracts/{contractId}/text-chunks/{offset}/{length}",
        MimeType = "application/json")]
    [Description("Reads an at-most 8 KiB UTF-8-aligned contract chunk as a JSON text envelope, avoiding client-side base64 decoding and hashing requirements.")]
    public static string GetContractResourceTextChunk(
        string contractId,
        int offset,
        int length)
    {
        if (!ContractSnapshots.Value.TryGetValue(contractId, out var resource))
        {
            throw new McpProtocolException(
                $"Contract resource '{contractId}' is not available. Read {ContractIndexResourceUri} for valid IDs.",
                McpErrorCode.ResourceNotFound);
        }

        var (text, end) = GetContractUtf8TextSlice(resource.Bytes, offset, length);
        var byteLength = end - offset;
        var uri = $"wpf://contracts/{contractId}/text-chunks/{offset}/{length}";
        return JsonSerializer.Serialize(new
        {
            contractId,
            resourceUri = uri,
            offset,
            byteLength,
            nextOffset = end,
            complete = end == resource.Bytes.Length,
            resourceByteLength = resource.Bytes.Length,
            resourceSha256 = resource.Sha256,
            text
        }, JsonResourceSerializerOptions);
    }

    internal static (string Text, int NextOffset) GetContractUtf8TextSlice(
        byte[] bytes,
        int offset,
        int length)
    {
        if (offset < 0
            || offset >= bytes.Length
            || length <= 0
            || length > MaxContractTextChunkBytes
            || !IsUtf8Boundary(bytes, offset))
        {
            throw new McpProtocolException(
                $"Contract text chunk requires a UTF-8 boundary offset within the {bytes.Length}-byte resource and length from 1 to {MaxContractTextChunkBytes} bytes.",
                McpErrorCode.InvalidParams);
        }

        var end = Math.Min(offset + length, bytes.Length);
        while (end > offset && !IsUtf8Boundary(bytes, end))
        {
            end--;
        }

        if (end == offset)
        {
            throw new McpProtocolException(
                "Contract text chunk length is too small to include the next complete UTF-8 character.",
                McpErrorCode.InvalidParams);
        }

        return (Encoding.UTF8.GetString(bytes, offset, end - offset), end);
    }

    private static bool IsUtf8Boundary(byte[] bytes, int offset) =>
        offset == 0
        || offset == bytes.Length
        || (bytes[offset] & 0xC0) != 0x80;

    private static IReadOnlyDictionary<string, ContractResourceSnapshot> CreateContractSnapshots()
    {
        var resources = new[]
        {
            ContractResourceSnapshot.Create("response", ResponseContractResourceUri, GetResponseContract()),
            ContractResourceSnapshot.Create("tools", ToolManifestResourceUri, GetToolManifest()),
            ContractResourceSnapshot.Create("tool-examples", ToolExamplesResourceUri, GetToolExamples())
        };
        return resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
    }

    private sealed record ContractResourceSnapshot(
        string Id,
        string ResourceUri,
        byte[] Bytes,
        string Sha256)
    {
        public static ContractResourceSnapshot Create(string id, string resourceUri, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new ContractResourceSnapshot(id, resourceUri, bytes, sha256);
        }
    }
}
