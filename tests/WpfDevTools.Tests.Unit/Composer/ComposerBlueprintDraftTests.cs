using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintDraftTests
{
    [Fact]
    public void DraftStore_ShouldBoundSizeCountAndLifetime()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new BlueprintDraftStore(
            maxDrafts: 2,
            maxCharacters: 256,
            lifetime: TimeSpan.FromMinutes(5),
            utcNow: () => now);

        var first = store.Create("{\"name\":\"first\"}");
        var second = store.Create("{\"name\":\"second\"}");
        var third = store.Create("{\"name\":\"third\"}");

        first.Success.Should().BeTrue();
        first.DraftRef.Should().StartWith(BlueprintDraftStore.ReferencePrefix);
        store.Resolve(first.DraftRef).Error!.Code.Should().Be("BlueprintDraftNotFound");
        store.Resolve(second.DraftRef).Success.Should().BeTrue();
        store.Resolve(third.DraftRef).Success.Should().BeTrue();
        store.Count.Should().Be(2);

        store.Create("{\"value\":\"" + new string('x', 300) + "\"}")
            .Error!.Code.Should().Be("BlueprintDraftTooLarge");

        now = now.AddMinutes(6);
        store.Resolve(third.DraftRef).Error!.Code.Should().Be("BlueprintDraftNotFound");
        store.Count.Should().Be(0);
    }

    [Fact]
    public void DraftStore_MergePatchShouldCreateAnImmutableDerivedDraft()
    {
        var store = new BlueprintDraftStore(4, 1024, TimeSpan.FromMinutes(5));
        var original = store.Create("""{"name":"Original","metadata":{"keep":true,"remove":"value"}}""");

        var patched = store.ApplyMergePatch(
            original.DraftRef,
            """{"name":"Derived","metadata":{"remove":null,"added":42}}""");

        patched.Success.Should().BeTrue(patched.Error?.Message);
        patched.DraftRef.Should().NotBe(original.DraftRef);
        store.Resolve(original.DraftRef).BlueprintJson.Should().Contain("Original").And.Contain("remove");
        store.Resolve(patched.DraftRef).BlueprintJson.Should().Contain("Derived").And.Contain("added").And.NotContain("remove");
    }

    [Fact]
    public async Task DraftTools_ShouldPatchComposeAndReuseOpaqueReferencesAcrossComposerWorkflow()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-draft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        try
        {
            var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
                Blueprint(),
                CancellationToken.None);
            created.IsError.Should().BeFalse();
            var createdPayload = created.StructuredContent!.Value;
            var originalRef = createdPayload.GetProperty("draftRef").GetString()!;
            createdPayload.TryGetProperty("blueprintJson", out _).Should().BeFalse();

            var patched = await UiComposerMcpTools.PatchUiBlueprintDraft(
                originalRef,
                """{"name":"Derived Draft","layout":{"properties":{"orientation":"Horizontal"}}}""",
                CancellationToken.None);
            patched.IsError.Should().BeFalse();
            var patchedRef = patched.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
            patchedRef.Should().NotBe(originalRef);

            var validation = await UiComposerMcpTools.ValidateUiBlueprint(
                patchedRef,
                cancellationToken: CancellationToken.None);
            validation.StructuredContent!.Value.GetProperty("valid").GetBoolean().Should().BeTrue();

            var render = await UiComposerMcpTools.RenderUiBlueprint(
                patchedRef,
                cancellationToken: CancellationToken.None);
            render.StructuredContent!.Value.GetProperty("xaml").GetString().Should().Contain("Orientation=\"Horizontal\"");

            var repair = await UiComposerMcpTools.RepairUiBlueprint(
                patchedRef,
                cancellationToken: CancellationToken.None);
            repair.IsError.Should().BeFalse();
            repair.StructuredContent!.Value.GetProperty("blueprintDraftRef").GetString().Should().Be(patchedRef);

            var apply = await UiComposerMcpTools.ApplyUiBlueprint(
                patchedRef,
                projectRoot,
                "Views/Draft.xaml",
                cancellationToken: CancellationToken.None);
            apply.IsError.Should().BeFalse();
            apply.StructuredContent!.Value.GetProperty("xaml").GetString().Should().Contain("Orientation=\"Horizontal\"");

            var composed = await UiComposerMcpTools.ComposeUiBlueprint(
                patchedRef,
                "$.layout.slots.children",
                "core.text",
                cancellationToken: CancellationToken.None);
            composed.IsError.Should().BeFalse();
            var composedPayload = composed.StructuredContent!.Value;
            composedPayload.GetProperty("composed").GetBoolean().Should().BeTrue(composedPayload.GetRawText());
            composedPayload.GetProperty("draftDerived").GetBoolean().Should().BeTrue();
            var composedRef = composedPayload.GetProperty("draftRef").GetString()!;
            composedRef.Should().NotBe(patchedRef);
            composedPayload.TryGetProperty("blueprint", out _).Should().BeFalse();

            var composedRender = await UiComposerMcpTools.RenderUiBlueprint(
                composedRef,
                cancellationToken: CancellationToken.None);
            composedRender.StructuredContent!.Value.GetProperty("xaml").GetString().Should().Contain("TextBlock");

            var originalRender = await UiComposerMcpTools.RenderUiBlueprint(
                patchedRef,
                cancellationToken: CancellationToken.None);
            originalRender.StructuredContent!.Value.GetProperty("xaml").GetString().Should().NotContain("TextBlock");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public async Task DraftReference_WhenMissing_ShouldReturnStructuredRecovery()
    {
        var result = await UiComposerMcpTools.ValidateUiBlueprint(
            BlueprintDraftStore.ReferencePrefix + "missing",
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("BlueprintDraftNotFound");
        error.GetProperty("repairSuggestion").GetString().Should().Contain("create_ui_blueprint_draft");
    }

    [Fact]
    public async Task PatchDraft_WhenInputIsInvalid_ShouldIdentifyTheExactRequestField()
    {
        var missing = await UiComposerMcpTools.PatchUiBlueprintDraft(
            BlueprintDraftStore.ReferencePrefix + "missing",
            "{}",
            CancellationToken.None);
        missing.StructuredContent!.Value.GetProperty("errors")[0]
            .GetProperty("jsonPath").GetString().Should().Be("$.draftRef");

        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            Blueprint(),
            CancellationToken.None);
        var draftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        var invalidPatch = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            "[]",
            CancellationToken.None);
        invalidPatch.StructuredContent!.Value.GetProperty("errors")[0]
            .GetProperty("jsonPath").GetString().Should().Be("$.patchJson");
    }

    private static string Blueprint()
        => """
           {
             "schemaVersion":"wpfdevtools.ui-blueprint.v1",
             "name":"Original Draft",
             "packs":[
               {"id":"core","version":"0.1.0","required":true,"role":"layout-pack"},
               {"id":"wpfui","version":"0.1.0","required":true,"role":"primary"}
             ],
             "primaryPack":"wpfui",
             "layout":{
               "kind":"core.stack",
               "properties":{"orientation":"Vertical"},
               "slots":{"children":[{"kind":"wpfui.button","properties":{"text":"Draft action"}}]}
             }
           }
           """;
}
