using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintCompositionMapTests
{
    [Fact]
    public async Task ValidateUiBlueprintTool_ShouldReturnCopyReadySlotOccupancyMap()
    {
        var result = await UiComposerMcpTools.ValidateUiBlueprint(
            """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "CompositionMap",
              "packs": [{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "core",
              "layout": {
                "kind": "core.border",
                "elementName": "Surface",
                "slots": {
                  "content": [{
                    "kind": "core.stack",
                    "elementName": "Body",
                    "slots": {
                      "children": [{ "kind": "core.text", "properties": { "text": "Ready" } }]
                    }
                  }]
                }
              }
            }
            """,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("valid").GetBoolean().Should().BeTrue();

        var map = payload.GetProperty("compositionMap");
        map.GetProperty("totalTargetCount").GetInt32().Should().Be(2);
        map.GetProperty("reportedTargetCount").GetInt32().Should().Be(2);
        map.GetProperty("truncated").GetBoolean().Should().BeFalse();

        var targets = map.GetProperty("targets").EnumerateArray().ToArray();
        var content = targets.Single(target => target.GetProperty("targetPath").GetString() == "@Surface.slots.content");
        content.GetProperty("parentJsonPath").GetString().Should().Be("$.layout");
        content.GetProperty("parentKind").GetString().Should().Be("core.border");
        content.GetProperty("slotName").GetString().Should().Be("content");
        content.GetProperty("targetPath").GetString().Should().Be("@Surface.slots.content");
        content.GetProperty("currentCount").GetInt32().Should().Be(1);
        content.GetProperty("maxItems").GetInt32().Should().Be(1);
        content.GetProperty("remainingCapacity").GetInt32().Should().Be(0);

        var children = targets.Single(target => target.GetProperty("targetPath").GetString() == "@Body.slots.children");
        children.GetProperty("currentCount").GetInt32().Should().Be(1);
        children.GetProperty("maxItems").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        children.GetProperty("remainingCapacity").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task ValidateUiBlueprintTool_ShouldBoundThirdPartySlotTargetsAndKeepPathsComposable()
    {
        var projectRoot = CreateProjectWithWideThirdPartyPack();
        try
        {
            var blueprint = """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "WidePack",
                  "packs": [{ "id": "wide", "version": "1.0.0", "required": true, "role": "primary" }],
                  "primaryPack": "wide",
                  "layout": { "kind": "wide.panel", "elementName": "Surface", "slots": {} }
                }
                """;
            var validation = await UiComposerMcpTools.ValidateUiBlueprint(
                blueprint,
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            validation.IsError.Should().BeFalse();
            var map = validation.StructuredContent!.Value.GetProperty("compositionMap");
            map.GetProperty("totalTargetCount").GetInt32().Should().Be(65);
            map.GetProperty("reportedTargetCount").GetInt32().Should().Be(64);
            map.GetProperty("truncated").GetBoolean().Should().BeTrue();
            map.GetProperty("targets").GetArrayLength().Should().Be(64);

            var dottedTarget = map.GetProperty("targets").EnumerateArray()
                .Single(target => target.GetProperty("slotName").GetString() == "action.area")
                .GetProperty("targetPath").GetString()!;
            var dottedComposition = await UiComposerMcpTools.ComposeUiBlueprint(
                blueprint,
                dottedTarget,
                "wide.leaf",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            var dottedPayload = dottedComposition.StructuredContent!.Value;
            dottedComposition.IsError.Should().BeFalse(dottedPayload.GetRawText());
            dottedPayload.GetProperty("insertedPath").GetString()
                .Should().Be("$.layout.slots[\"action.area\"][0]");

            const string escapedSlotName = "action.\"area\\zone";
            var escapedTarget = map.GetProperty("targets").EnumerateArray()
                .Single(target => target.GetProperty("slotName").GetString() == escapedSlotName)
                .GetProperty("targetPath").GetString()!;
            var escapedComposition = await UiComposerMcpTools.ComposeUiBlueprint(
                blueprint,
                escapedTarget,
                "wide.leaf",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            escapedComposition.IsError.Should().BeFalse(escapedComposition.StructuredContent!.Value.GetRawText());
            escapedComposition.StructuredContent!.Value.GetProperty("insertedPath").GetString()
                .Should().Be($"$.layout.slots[{JsonSerializer.Serialize(escapedSlotName)}][0]");
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateUiBlueprintTool_ShouldIncludeUnnamedNestedThirdPartyContainerSlots()
    {
        var projectRoot = CreateProjectWithWideThirdPartyPack(regularSlotCount: 1);
        try
        {
            var result = await UiComposerMcpTools.ValidateUiBlueprint(
                """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "UnnamedNested",
                  "packs": [{ "id": "wide", "version": "1.0.0", "required": true, "role": "primary" }],
                  "primaryPack": "wide",
                  "layout": {
                    "kind": "wide.panel",
                    "slots": { "slot00": [{ "kind": "wide.panel", "slots": {} }] }
                  }
                }
                """,
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var map = result.StructuredContent!.Value.GetProperty("compositionMap");
            map.GetProperty("totalTargetCount").GetInt32().Should().Be(6);
            map.GetProperty("targets").EnumerateArray().Should().Contain(target =>
                target.GetProperty("parentJsonPath").GetString() == "$.layout.slots.slot00[0]"
                && target.GetProperty("targetPath").GetString()
                    == "$.layout.slots.slot00[0].slots[\"action.area\"]");
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    private static string CreateProjectWithWideThirdPartyPack(int regularSlotCount = 63)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-wide-pack-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(projectRoot, ".wpfdevtools", "packs", "wide", "1.0.0");
        Directory.CreateDirectory(Path.Combine(root, "blocks"));
        Directory.CreateDirectory(Path.Combine(root, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(root, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"wide","displayName":"Wide","version":"1.0.0","kind":"control-pack","blocks":["wide.panel","wide.leaf"],"recipes":[],"xmlNamespaces":{}}""");
        File.WriteAllText(Path.Combine(root, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Wide","url":"https://example.invalid/wide","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");

        var slots = new JsonObject
        {
            ["action.area"] = CreateSlotContract(),
            ["action.\"area\\zone"] = CreateSlotContract()
        };
        for (var index = 0; index < regularSlotCount; index++)
        {
            slots[$"slot{index:D2}"] = CreateSlotContract();
        }

        var panel = new JsonObject
        {
            ["schemaVersion"] = "wpfdevtools.ui-block.v1",
            ["kind"] = "wide.panel",
            ["displayName"] = "Panel",
            ["category"] = "container",
            ["properties"] = new JsonObject(),
            ["slots"] = slots,
            ["renderer"] = new JsonObject { ["xamlTemplate"] = "renderers/xaml/panel.xaml.sbn" },
            ["sourceHints"] = new JsonArray()
        };
        File.WriteAllText(Path.Combine(root, "blocks", "panel.block.json"), panel.ToJsonString());
        File.WriteAllText(Path.Combine(root, "blocks", "leaf.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"wide.leaf","displayName":"Leaf","category":"content","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/leaf.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(root, "renderers", "xaml", "panel.xaml.sbn"), "<Grid>{{slot.action.area}}</Grid>");
        File.WriteAllText(Path.Combine(root, "renderers", "xaml", "leaf.xaml.sbn"), "<TextBlock Text=\"Leaf\" />");
        var escapedRoot = root.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(root, "install.manifest.json"),
            $$"""{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"wide","version":"1.0.0","scope":"project-local","path":"{{escapedRoot}}","enabled":true}""");
        return projectRoot;
    }

    private static JsonObject CreateSlotContract()
        => new()
        {
            ["allowedKinds"] = new JsonArray(new JsonNode?[]
            {
                JsonValue.Create("wide.leaf"),
                JsonValue.Create("wide.panel")
            }),
            ["minItems"] = 0,
            ["maxItems"] = 1
        };
}
