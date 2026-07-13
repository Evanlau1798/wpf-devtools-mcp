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

    [Fact]
    public async Task PreviewUiBlueprintTool_ShouldNotThrowWhenDeclaredPacksCollideOnBlockKind()
    {
        var projectRoot = CreateCollidingBlockPack();
        try
        {
            using var sessionManager = new SessionManager();
            var blueprint = """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "CollisionHints",
                  "packs": [
                    { "id": "core", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "shadow", "version": "1.0.0", "required": true, "role": "extension" }
                  ],
                  "primaryPack": "core",
                  "layout": { "kind": "core.stack", "properties": { "spacing": "4" } }
                }
                """;

            var result = await UiComposerMcpTools.PreviewUiBlueprint(
                sessionManager,
                blueprint,
                restoreEnabled: false,
                projectRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse(result.StructuredContent?.GetRawText());
            result.StructuredContent!.Value.GetProperty("propertyWarnings")
                .GetArrayLength().Should().Be(1);
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewUiBlueprintTool_ShouldPreserveWarningsWhenPreviewContractIsInvalid()
    {
        var projectRoot = CreateInvalidPreviewHintPack();
        try
        {
            using var sessionManager = new SessionManager();
            var blueprint = """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "InvalidPreviewHints",
                  "packs": [{ "id": "sample", "version": "1.0.0", "required": true, "role": "primary" }],
                  "primaryPack": "sample",
                  "layout": { "kind": "sample.panel", "properties": { "caption": "Ready" } }
                }
                """;

            var result = await UiComposerMcpTools.PreviewUiBlueprint(
                sessionManager,
                blueprint,
                restoreEnabled: false,
                projectRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeTrue();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("diagnostics")[0].GetProperty("code").GetString()
                .Should().Be("PreviewContractMissing");
            payload.GetProperty("propertyWarnings").GetArrayLength().Should().Be(1);
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
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"hint","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
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

    private static string CreateCollidingBlockPack()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-collision-hints-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "shadow", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"shadow","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"shadow","displayName":"Shadow","version":"1.0.0","blocks":["core.stack"],"recipes":[],"xmlNamespaces":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Shadow","url":"https://example.invalid/shadow","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "stack.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"core.stack","displayName":"Shadow Stack","description":"Collision fixture.","category":"container","properties":{"orientation":{"type":"string","default":"Vertical"},"spacing":{"type":"string","default":"0","previewWarning":"Validate the final app."},"margin":{"type":"string","default":"0"},"horizontalAlignment":{"type":"string","default":"Stretch"},"verticalAlignment":{"type":"string","default":"Stretch"}},"slots":{"children":{"allowedKinds":["*"]}},"renderer":{"xamlTemplate":"renderers/xaml/stack.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "stack.xaml.sbn"),
            "<StackPanel Margin=\"{{spacing}}\" />");
        return projectRoot;
    }

    private static string CreateInvalidPreviewHintPack()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-invalid-preview-hints-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","displayName":"Sample","version":"1.0.0","blocks":["sample.panel"],"recipes":[],"xmlNamespaces":{"sample":"urn:sample-controls"}}""");
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Sample","url":"https://example.invalid/sample","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "panel.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.panel","displayName":"Panel","description":"Invalid preview fixture.","category":"container","properties":{"caption":{"type":"string","previewWarning":"Validate the final app."}},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/panel.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn"),
            "<sample:Panel Caption=\"{{caption}}\" />");
        return projectRoot;
    }
}
