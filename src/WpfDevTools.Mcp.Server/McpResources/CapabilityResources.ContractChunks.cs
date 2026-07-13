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
                chunkUriTemplate = $"wpf://contracts/{resource.Id}/chunks/{{offset}}/{{length}}"
            });
        return JsonSerializer.Serialize(new
        {
            resourceUri = ContractIndexResourceUri,
            version = "1.0",
            maxChunkBytes = MaxContractChunkBytes,
            encoding = "utf-8",
            chunkMimeType = "application/octet-stream",
            reconstruction = "Read sequential chunks from offset 0, concatenate decoded bytes without transformation, then verify byteLength and sha256 before parsing JSON.",
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
