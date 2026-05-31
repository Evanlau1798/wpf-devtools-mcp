using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpResources;

public static partial class CapabilityResources
{
    private const string ToolManifestResourceUri = "wpf://contracts/tools";

    [McpServerResource(
        Name = "wpf_tool_manifest",
        Title = "Tool Manifest",
        UriTemplate = ToolManifestResourceUri,
        MimeType = "application/json")]
    [Description("Canonical MCP tool manifest generated from source tool attributes and method signatures.")]
    public static string GetToolManifest()
    {
        var manifest = CanonicalMcpToolManifest.GetManifest(ToolManifestResourceUri);
        return JsonSerializer.Serialize(manifest, JsonResourceSerializerOptions);
    }
}
