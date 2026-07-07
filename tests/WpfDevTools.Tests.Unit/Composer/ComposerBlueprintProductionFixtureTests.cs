using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintProductionFixtureTests
{
    [Theory]
    [MemberData(nameof(ValidBlueprints))]
    public void ValidateBlueprint_ShouldAcceptProductionValidFixtures(string blueprintJson)
    {
        var result = CreateValidator().Validate(blueprintJson);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidBlueprints))]
    public void ValidateBlueprint_ShouldRejectProductionInvalidFixtures(string blueprintJson, string expectedCode)
    {
        var result = CreateValidator().Validate(blueprintJson);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == expectedCode
            && !string.IsNullOrWhiteSpace(issue.JsonPath)
            && !string.IsNullOrWhiteSpace(issue.RepairSuggestion));
    }

    public static TheoryData<string> ValidBlueprints()
        => new()
        {
            Blueprint("""{ "kind": "wpfui.button", "properties": { "text": "Save" }, "slots": { "icon": [{ "kind": "wpfui.symbolIcon" }] } }"""),
            Blueprint("""{ "kind": "wpfui.card", "slots": { "header": [{ "kind": "text", "properties": { "value": "Status" } }], "content": [{ "kind": "text", "properties": { "value": "Ready" } }], "actions": [{ "kind": "wpfui.button" }] } }"""),
            Blueprint("""{ "kind": "wpfui.navigationView", "slots": { "items": [{ "kind": "wpfui.navigationViewItem", "slots": { "content": [{ "kind": "text", "properties": { "value": "Home" } }], "icon": [{ "kind": "wpfui.symbolIcon" }] } }], "content": [{ "kind": "wpfui.card" }] } }"""),
            Blueprint("""{ "kind": "wpfui.tabView", "slots": { "items": [{ "kind": "wpfui.tabViewItem", "slots": { "header": [{ "kind": "text", "properties": { "value": "General" } }], "content": [{ "kind": "wpfui.card" }] } }] } }"""),
            Blueprint("""{ "kind": "wpfui.contentDialog", "slots": { "content": [{ "kind": "text", "properties": { "value": "Continue?" } }], "actions": [{ "kind": "wpfui.button" }] } }"""),
            Blueprint("""{ "kind": "wpfui.dataGrid", "properties": { "itemsSource": "{Binding Rows}" }, "slots": { "columns": [{ "kind": "template" }], "emptyState": [{ "kind": "wpfui.textBlock" }] } }"""),
            Blueprint("""{ "kind": "wpfui.snackbar", "slots": { "content": [{ "kind": "wpfui.textBlock" }], "actions": [{ "kind": "wpfui.button" }] } }"""),
            Blueprint("""{ "kind": "wpfui.fluentWindow", "slots": { "titleBar": [{ "kind": "wpfui.titleBar" }], "content": [{ "kind": "wpfui.navigationView" }] } }"""),
            Blueprint("""{ "kind": "wpfui.textBlock", "properties": { "text": "Hello", "appearance": "Body" } }"""),
            Blueprint("""{ "kind": "wpfui.card", "slots": { "content": [{ "kind": "stack", "slots": { "stack": [{ "kind": "text", "properties": { "value": "Line" } }, { "kind": "template" }] } }] } }""")
        };

    public static TheoryData<string, string> InvalidBlueprints()
        => new()
        {
            { """{ "name": "MissingSchema", "packs": [], "primaryPack": "wpfui", "layout": { "kind": "wpfui.button" } }""", "InvalidBlueprintJson" },
            { Raw("""{ "schemaVersion": "wpfdevtools.ui-blueprint.v1", "name": "NoPacks", "primaryPack": "wpfui", "layout": { "kind": "wpfui.button" } }"""), "RequiredFieldMissing" },
            { Raw("""{ "schemaVersion": "wpfdevtools.ui-blueprint.v1", "name": "NoPrimary", "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }], "layout": { "kind": "wpfui.button" } }"""), "RequiredFieldMissing" },
            { Raw("""{ "schemaVersion": "wpfdevtools.ui-blueprint.v1", "name": "WrongRole", "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "optional" }], "primaryPack": "wpfui", "layout": { "kind": "wpfui.button" } }"""), "PrimaryPackRoleMismatch" },
            { Blueprint("""{ "kind": "button" }"""), "UnqualifiedBlockKind" },
            { Blueprint("""{ "kind": "wpfui.missing" }"""), "UnknownBlockKind" },
            { Blueprint("""{ "kind": "unknown.button" }"""), "PackNotDeclared" },
            { Blueprint("""{ "kind": "wpfui.card", "slots": { "missing": [{ "kind": "wpfui.button" }] } }"""), "UnknownSlot" },
            { Blueprint("""{ "kind": "wpfui.navigationView", "slots": { "items": [{ "kind": "wpfui.button" }] } }"""), "SlotChildKindNotAllowed" },
            { Blueprint("""{ "kind": "wpfui.button", "properties": { "rawXaml": "<Button />" } }"""), "UnknownProperty" },
            { Blueprint("""{ "kind": "wpfui.card", "slots": { "content": [{ "kind": "stack", "slots": { "stack": [{ "kind": "text", "properties": { "value": "Line" } }, { "kind": "wpfui.button", "properties": { "rawXaml": "<Button />" } }] } }] } }"""), "UnknownProperty" },
            { Blueprint("""{ "kind": "wpfui.snackbar", "properties": { "timeout": "slow" } }"""), "PropertyTypeMismatch" }
        };

    private static BlueprintValidationService CreateValidator()
        => new(PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath(".")));

    private static string Blueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ProductionFixture",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;

    private static string Raw(string json)
        => json;
}
