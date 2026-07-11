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

    [Fact]
    public async Task PreviewUiBlueprintTool_ShouldEscapeThirdPartyWarningPaths()
    {
        var projectRoot = CreateDottedAuthoringHintPack();
        try
        {
            using var sessionManager = new SessionManager();
            var blueprint = """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "DottedHints",
                  "packs": [{ "id": "hint", "version": "1.0.0", "required": true, "role": "primary" }],
                  "primaryPack": "hint",
                  "layout": {
                    "kind": "hint.container",
                    "slots": {
                      "body.items": [{
                        "kind": "hint.container",
                        "properties": { "layout.gap": "4" }
                      }]
                    }
                  }
                }
                """;

            var result = await UiComposerMcpTools.PreviewUiBlueprint(
                sessionManager,
                blueprint,
                restoreEnabled: false,
                projectRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.StructuredContent!.Value.GetProperty("propertyWarnings")[0]
                .GetProperty("jsonPath").GetString().Should()
                .Be("$.layout.slots[\"body.items\"][0].properties[\"layout.gap\"]");
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    private static string CreateDottedAuthoringHintPack()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-dotted-hints-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "hint", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"hint","version":"1.0.0","scope":"project","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"hint","displayName":"Hint","version":"1.0.0","blocks":["hint.container"],"recipes":[],"xmlNamespaces":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Hint","url":"https://example.invalid/hint","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "container.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"hint.container","displayName":"Container","description":"Test container.","category":"container","properties":{"layout.gap":{"type":"string","default":"0","previewWarning":"Validate the final app."}},"slots":{"body.items":{"allowedKinds":["hint.container"]}},"renderer":{"xamlTemplate":"renderers/xaml/container.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "container.xaml.sbn"),
            "<StackPanel Margin=\"{{layout.gap}}\">{{slot.body.items}}</StackPanel>");
        return projectRoot;
    }
}
