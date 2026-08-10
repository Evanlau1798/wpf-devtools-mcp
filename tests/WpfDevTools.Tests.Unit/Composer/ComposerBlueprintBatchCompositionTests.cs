using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintBatchCompositionTests
{
    [Fact]
    public async Task ComposeUiBlueprint_ShouldApplyDependentOperationsIntoOneDerivedDraft()
    {
        var sourceRef = await CreateDraftAsync();

        var result = await UiComposerMcpTools.ComposeUiBlueprint(
            sourceRef,
            operations:
            [
                Operation("@Root.slots.children", "core.stack", "Panel"),
                Operation("@Panel.slots.children", "core.text", "Title", new { text = "Ready" })
            ],
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse(result.StructuredContent?.GetRawText());
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("batchComposed").GetBoolean().Should().BeTrue();
        payload.GetProperty("operationCount").GetInt32().Should().Be(2);
        payload.GetProperty("validation").GetProperty("valid").GetBoolean().Should().BeTrue();
        payload.GetProperty("operations").EnumerateArray()
            .Select(item => item.GetProperty("operationIndex").GetInt32())
            .Should().Equal(0, 1);
        payload.GetProperty("sourceDraftRef").GetString().Should().Be(sourceRef);
        var derivedRef = payload.GetProperty("draftRef").GetString()!;
        derivedRef.Should().NotBe(sourceRef);
        BlueprintInputResolver.Store.Resolve(sourceRef).BlueprintJson.Should().NotContain("Panel");
        BlueprintInputResolver.Store.Resolve(derivedRef).BlueprintJson.Should()
            .Contain("Panel").And.Contain("Title").And.Contain("Ready");
    }

    [Fact]
    public async Task ComposeUiBlueprint_WhenDependentOperationFails_ShouldRemainAtomic()
    {
        var sourceRef = await CreateDraftAsync();

        var result = await UiComposerMcpTools.ComposeUiBlueprint(
            sourceRef,
            operations:
            [
                Operation("@Root.slots.children", "core.stack", "Panel"),
                Operation("@Missing.slots.children", "core.text", "Title")
            ],
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("failedOperationIndex").GetInt32().Should().Be(1);
        payload.TryGetProperty("draftRef", out _).Should().BeFalse();
        BlueprintInputResolver.Store.Resolve(sourceRef).BlueprintJson.Should().NotContain("Panel");
    }

    [Fact]
    public void ComposeUiBlueprint_ShouldPublishBoundedBatchOperationsSchema()
    {
        var method = typeof(UiComposerMcpTools).GetMethod(nameof(UiComposerMcpTools.ComposeUiBlueprint))!;
        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema test does not invoke tools."))
            .BuildServiceProvider();
        var tool = McpServerTool.Create(
            method,
            target: null,
            new McpServerToolCreateOptions { Services = services }).ProtocolTool;
        McpToolInputSchemaNormalizer.Apply(tool);

        var operations = tool.InputSchema.GetProperty("properties").GetProperty("operations");
        operations.GetProperty("minItems").GetInt32().Should().Be(1);
        operations.GetProperty("maxItems").GetInt32().Should().Be(16);
        operations.GetProperty("items").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name)
            .Should().Contain(["targetPath", "kind", "elementName", "automationId", "properties", "insertionIndex"]);
    }

    private static async Task<string> CreateDraftAsync()
    {
        var result = await UiComposerMcpTools.CreateUiBlueprintDraft(
            """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "BatchComposition",
              "packs": [{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "core",
              "layout": {
                "kind": "core.stack",
                "elementName": "Root",
                "slots": { "children": [] }
              }
            }
            """,
            CancellationToken.None);
        return result.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
    }

    private static BlueprintCompositionOperation Operation(
        string targetPath,
        string kind,
        string elementName,
        object? properties = null)
        => new()
        {
            TargetPath = targetPath,
            Kind = kind,
            ElementName = elementName,
            Properties = properties is null ? null : JsonSerializer.SerializeToElement(properties)
        };
}
