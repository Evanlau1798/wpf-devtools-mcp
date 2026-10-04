using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using System.Text.Json;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerDraftExpirationMetadataTests
{
    [Theory]
    [InlineData("single")]
    [InlineData("batch")]
    [InlineData("candidate")]
    public async Task CompositionDerivedDrafts_ShouldReportRemainingLifetime(string mode)
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft("""
            {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"Lifetime",
             "packs":[{"id":"core","version":"0.1.0","required":true,"role":"primary"}],
             "primaryPack":"core","layout":{"kind":"core.stack","slots":{"children":[]}}}
            """);
        var source = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        var result = mode == "batch"
            ? await UiComposerMcpTools.ComposeUiBlueprint(source, operations:
                [new BlueprintCompositionOperation { TargetPath = "$.layout.slots.children", Kind = "core.text" }])
            : await UiComposerMcpTools.ComposeUiBlueprint(source, "$.layout.slots.children", "core.text",
                properties: mode == "candidate" ? JsonSerializer.SerializeToElement(new { unknownForLifetime = true }) : null);
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("composed").GetBoolean().Should().Be(mode != "candidate", payload.GetRawText());
        payload.GetProperty(mode == "candidate" ? "candidateDraftRef" : "draftRef")
            .GetString().Should().NotBe(source);
        VerifyLifetime(payload);
    }

    [Fact]
    public async Task CreateAndExport_ShouldReportUtcTimeAndRemainingLifetime()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft("{\"name\":\"Scratch\"}");
        var payload = created.StructuredContent!.Value;
        VerifyLifetime(payload);
        var exported = await UiComposerMcpTools.GetUiBlueprintDraft(payload.GetProperty("draftRef").GetString()!);
        VerifyLifetime(exported.StructuredContent!.Value);
    }

    private static void VerifyLifetime(System.Text.Json.JsonElement payload)
    {
        var now = payload.GetProperty("serverTimeUtc").GetDateTimeOffset();
        var expiresAt = payload.GetProperty("expiresAt").GetDateTimeOffset();
        now.Offset.Should().Be(TimeSpan.Zero);
        var remaining = payload.GetProperty("expiresInSeconds").GetInt32();
        remaining.Should().BeInRange(14390, 14400);
        Math.Abs(remaining - (expiresAt - now).TotalSeconds).Should().BeLessThanOrEqualTo(1);
    }
}
