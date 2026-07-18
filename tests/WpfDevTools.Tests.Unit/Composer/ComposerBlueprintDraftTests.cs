using FluentAssertions;
using System.Text.Json;
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
    public void DraftStore_PathUpdateShouldCreateAnImmutableDerivedDraftWithCompactChangeSummary()
    {
        var store = new BlueprintDraftStore(4, 4096, TimeSpan.FromMinutes(5));
        var original = store.Create(Blueprint());
        using var value = JsonDocument.Parse("\"Print slip\"");

        var updated = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.slots.children[0].properties.text",
            value.RootElement,
            remove: false);

        updated.Success.Should().BeTrue(updated.Error?.Message);
        updated.DraftRef.Should().NotBe(original.DraftRef);
        store.Resolve(original.DraftRef).BlueprintJson.Should().Contain("Draft action");
        store.Resolve(updated.DraftRef).BlueprintJson.Should().Contain("Print slip");
        updated.ChangeSummary.Should().NotBeNull();
        updated.ChangeSummary!.ChangeCount.Should().Be(1);
        updated.ChangeSummary.Changes.Should().ContainSingle(change =>
            change.JsonPath == "$.layout.slots.children[0].properties.text"
            && change.ChangeType == "modified"
            && change.Before == "\"Draft action\""
            && change.After == "\"Print slip\"");
    }

    [Fact]
    public void DraftStore_PathUpdateShouldRemovePropertiesAndRejectInvalidRequests()
    {
        var store = new BlueprintDraftStore(8, 4096, TimeSpan.FromMinutes(5));
        var original = store.Create(Blueprint());

        var removed = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.slots.children[0].properties.text",
            value: null,
            remove: true);
        var missingTarget = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.slots.children[9].properties.text",
            JsonDocument.Parse("\"unused\"").RootElement,
            remove: false);
        var missingValue = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.properties.orientation",
            value: null,
            remove: false);

        removed.Success.Should().BeTrue(removed.Error?.Message);
        store.Resolve(removed.DraftRef).BlueprintJson.Should().NotContain("Draft action");
        removed.ChangeSummary!.Changes.Should().ContainSingle(change =>
            change.JsonPath == "$.layout.slots.children[0].properties.text"
            && change.ChangeType == "removed");
        missingTarget.Error!.Code.Should().Be("BlueprintDraftPathNotFound");
        missingValue.Error!.Code.Should().Be("BlueprintDraftValueRequired");
    }

    [Fact]
    public void DraftStore_PathUpdateShouldReportTheExactRemovedArrayItem()
    {
        var store = new BlueprintDraftStore(4, 4096, TimeSpan.FromMinutes(5));
        var original = store.Create("""{"items":["a","b"]}""");

        var removed = store.ApplyPathUpdate(
            original.DraftRef,
            "$.items[0]",
            value: null,
            remove: true);

        removed.Success.Should().BeTrue(removed.Error?.Message);
        store.Resolve(removed.DraftRef).BlueprintJson.Should().Be("""{"items":["b"]}""");
        removed.ChangeSummary!.Changes.Should().ContainSingle(change =>
            change.JsonPath == "$.items[0]"
            && change.ChangeType == "removed"
            && change.Before == "\"a\""
            && change.After == null);
    }

    [Fact]
    public void DraftStore_PathUpdateShouldDistinguishAnAddedJsonNullFromAMissingProperty()
    {
        var store = new BlueprintDraftStore(4, 4096, TimeSpan.FromMinutes(5));
        var original = store.Create(Blueprint());
        using var value = JsonDocument.Parse("null");

        var updated = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.properties.optionalNote",
            value.RootElement,
            remove: false);

        updated.Success.Should().BeTrue(updated.Error?.Message);
        updated.ChangeSummary!.Changes.Should().ContainSingle(change =>
            change.JsonPath == "$.layout.properties.optionalNote"
            && change.ChangeType == "added"
            && change.Before == null
            && change.After == "null");
    }

    [Fact]
    public void DraftStore_PathUpdateShouldSupportPackDefinedPropertyNamesThroughQuotedSegments()
    {
        var store = new BlueprintDraftStore(4, 4096, TimeSpan.FromMinutes(5));
        var original = store.Create("""{"layout":{"properties":{"accent.color":"old"}}}""");
        using var value = JsonDocument.Parse("\"new\"");

        var updated = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.properties[\"accent.color\"]",
            value.RootElement,
            remove: false);

        updated.Success.Should().BeTrue(updated.Error?.Message);
        store.Resolve(updated.DraftRef).BlueprintJson.Should().Contain("\"accent.color\":\"new\"");
        updated.ChangeSummary!.Changes.Should().ContainSingle(change =>
            change.JsonPath == "$.layout.properties[\"accent.color\"]");
    }

    [Fact]
    public void DraftStore_PathSetShouldCreateMissingObjectParentsWithoutGuessingArraysOrRemovals()
    {
        var store = new BlueprintDraftStore();
        var original = store.Create("""{"layout":{"kind":"core.text"}}""");
        using var value = JsonDocument.Parse("\"Observation\"");

        var updated = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.properties.text",
            value.RootElement,
            remove: false);
        var missingRemoval = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.properties.text",
            value: null,
            remove: true);
        var missingArray = store.ApplyPathUpdate(
            original.DraftRef,
            "$.layout.slots.children[0]",
            value.RootElement,
            remove: false);

        updated.Success.Should().BeTrue(updated.Error?.Message);
        store.Resolve(updated.DraftRef).BlueprintJson.Should()
            .Contain("\"properties\":{\"text\":\"Observation\"}");
        updated.ChangeSummary!.Changes.Should().ContainSingle(change =>
            change.JsonPath == "$.layout.properties.text" && change.ChangeType == "added");
        missingRemoval.Error!.Code.Should().Be("BlueprintDraftPathNotFound");
        missingArray.Error!.Code.Should().Be("BlueprintDraftPathNotFound");
    }

    [Fact]
    public async Task PatchDraft_PathModeShouldReturnCompactChangeSummaryWithoutBlueprintEcho()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            Blueprint(),
            CancellationToken.None);
        var originalRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        using var value = JsonDocument.Parse("\"Horizontal\"");

        var updated = await UiComposerMcpTools.PatchUiBlueprintDraft(
            originalRef,
            patchJson: null,
            jsonPath: "$.layout.properties.orientation",
            value: value.RootElement,
            cancellationToken: CancellationToken.None);

        updated.IsError.Should().BeFalse();
        var payload = updated.StructuredContent!.Value;
        payload.TryGetProperty("blueprintJson", out _).Should().BeFalse();
        payload.GetProperty("sourceDraftRef").GetString().Should().Be(originalRef);
        var summary = payload.GetProperty("changeSummary");
        summary.GetProperty("changeCount").GetInt32().Should().Be(1);
        summary.GetProperty("changes")[0].GetProperty("jsonPath").GetString()
            .Should().Be("$.layout.properties.orientation");
        summary.GetProperty("changes")[0].TryGetProperty("operationIndex", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PatchDraft_ArrayRemovalShouldPublishAnExplicitNullAfterValue()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            """{"items":["a","b"]}""",
            CancellationToken.None);
        var draftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;

        var removed = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            patchJson: null,
            jsonPath: "$.items[0]",
            value: null,
            remove: true,
            cancellationToken: CancellationToken.None);

        removed.IsError.Should().BeFalse(removed.StructuredContent?.GetRawText());
        var change = removed.StructuredContent!.Value.GetProperty("changeSummary")
            .GetProperty("changes")[0];
        change.GetProperty("jsonPath").GetString().Should().Be("$.items[0]");
        change.GetProperty("before").GetString().Should().Be("\"a\"");
        change.TryGetProperty("after", out var after).Should().BeTrue();
        after.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PatchDraft_ExplicitJsonNullDefaultsShouldFollowTheSelectedMode()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            """{"name":"old","removeMe":true}""",
            CancellationToken.None);
        var draftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        using var jsonNull = JsonDocument.Parse("null");

        var merged = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            patchJson: """{"name":"new"}""",
            jsonPath: null,
            value: jsonNull.RootElement,
            remove: false,
            cancellationToken: CancellationToken.None);
        var removed = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            patchJson: null,
            jsonPath: "$.removeMe",
            value: jsonNull.RootElement,
            remove: true,
            cancellationToken: CancellationToken.None);
        var setToNull = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            patchJson: null,
            jsonPath: "$.optional",
            value: jsonNull.RootElement,
            remove: false,
            cancellationToken: CancellationToken.None);

        merged.IsError.Should().BeFalse(merged.StructuredContent?.GetRawText());
        removed.IsError.Should().BeFalse(removed.StructuredContent?.GetRawText());
        setToNull.IsError.Should().BeFalse(setToNull.StructuredContent?.GetRawText());
        setToNull.StructuredContent!.Value.GetProperty("changeSummary")
            .GetProperty("changes")[0].GetProperty("after").GetString().Should().Be("null");
    }

    [Fact]
    public async Task PatchDraft_WhenPathValueExceedsTheDraftLimit_ShouldIdentifyValue()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            JsonSerializer.Serialize(new { padding = new string('x', 60_000) }),
            CancellationToken.None);
        var draftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        using var largeValue = JsonDocument.Parse(JsonSerializer.Serialize(new string('y', 8_000)));

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            patchJson: null,
            jsonPath: "$.extra",
            value: largeValue.RootElement,
            remove: false,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("BlueprintDraftTooLarge");
        error.GetProperty("jsonPath").GetString().Should().Be("$.value");
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
                cancellationToken: CancellationToken.None);
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
                includeGeneratedXaml: true,
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
            cancellationToken: CancellationToken.None);
        missing.StructuredContent!.Value.GetProperty("errors")[0]
            .GetProperty("jsonPath").GetString().Should().Be("$.draftRef");

        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            Blueprint(),
            CancellationToken.None);
        var draftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        var invalidPatch = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            "[]",
            cancellationToken: CancellationToken.None);
        invalidPatch.StructuredContent!.Value.GetProperty("errors")[0]
            .GetProperty("jsonPath").GetString().Should().Be("$.patchJson");

        using var ambiguousValue = JsonDocument.Parse("\"ambiguous\"");
        var ambiguous = await UiComposerMcpTools.PatchUiBlueprintDraft(
            draftRef,
            patchJson: "{}",
            jsonPath: "$.name",
            value: ambiguousValue.RootElement,
            cancellationToken: CancellationToken.None);
        ambiguous.StructuredContent!.Value.GetProperty("errors")[0]
            .GetProperty("code").GetString().Should().Be("BlueprintDraftMutationModeConflict");
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
