using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintDraftCheckpointTests
{
    [Fact]
    public async Task GetUiBlueprintDraft_ShouldExportAnExplicitRecreatableCheckpoint()
    {
        const string blueprint = """
            {
              "schemaVersion":"wpfdevtools.ui-blueprint.v1",
              "name":"Checkpoint",
              "packs":[{"id":"core","version":"0.1.0","required":true,"role":"primary"}],
              "primaryPack":"core",
              "layout":{"kind":"core.stack","elementName":"Root"}
            }
            """;
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            blueprint,
            CancellationToken.None);
        var draftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;

        var checkpoint = await UiComposerMcpTools.GetUiBlueprintDraft(
            draftRef,
            CancellationToken.None);

        checkpoint.IsError.Should().BeFalse();
        var payload = checkpoint.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("draftRef").GetString().Should().Be(draftRef);
        payload.GetProperty("blueprintJson").GetString().Should().Be(blueprint);
        payload.GetProperty("characterCount").GetInt32().Should().Be(blueprint.Length);
        payload.GetProperty("recreateWith").GetString().Should().Be("create_ui_blueprint_draft");
    }

    [Fact]
    public async Task GetUiBlueprintDraft_WhenReferenceIsMissing_ShouldReturnStructuredRecovery()
    {
        var result = await UiComposerMcpTools.GetUiBlueprintDraft(
            BlueprintDraftStore.ReferencePrefix + "missing",
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("jsonPath").GetString().Should().Be("$.draftRef");
        error.GetProperty("code").GetString().Should().Be("BlueprintDraftNotFound");
    }
}
