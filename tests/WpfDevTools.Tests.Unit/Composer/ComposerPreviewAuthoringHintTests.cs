using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerPreviewCompileTests
{
    [Fact]
    public async Task PreviewUiBlueprintTool_ShouldReturnPackDefinedWarningsAtExactPaths()
    {
        using var sessionManager = new SessionManager();
        var blueprint = Blueprint("""
            {
              "kind": "core.stack",
              "properties": { "orientation": "Horizontal", "spacing": "12" },
              "slots": { "children": [{ "kind": "core.text", "properties": { "text": "Ready" } }] }
            }
            """);

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            blueprint,
            restoreEnabled: false,
            cancellationToken: CancellationToken.None);

        var warnings = result.StructuredContent!.Value.GetProperty("propertyWarnings")
            .EnumerateArray()
            .ToArray();
        warnings.Should().ContainSingle();
        warnings[0].GetProperty("jsonPath").GetString().Should().Be("$.layout.properties.spacing");
        warnings[0].GetProperty("blockKind").GetString().Should().Be("core.stack");
        warnings[0].GetProperty("propertyName").GetString().Should().Be("spacing");
        warnings[0].GetProperty("message").GetString().Should().Contain("final app");
    }
}
