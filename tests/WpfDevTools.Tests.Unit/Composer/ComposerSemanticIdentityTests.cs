using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerSemanticIdentityTests
{
    [Fact]
    public void Renderer_ShouldPreserveAuthoredElementNameAndAutomationId()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var normal = renderer.Render(new RenderBlueprintRequest(ValidBlueprint()));
        var preview = renderer.Render(new RenderBlueprintRequest(
            ValidBlueprint(),
            IncludeTransientElementCorrelation: true));

        normal.Success.Should().BeTrue(normal.Errors.FirstOrDefault()?.Message);
        normal.Xaml.Should().Contain("x:Name=\"ComposerRoot\"");
        normal.Xaml.Should().Contain("AutomationProperties.AutomationId=\"composer-root\"");
        normal.Xaml.Should().Contain("x:Name=\"PrimaryAction\"");
        normal.Xaml.Should().Contain("AutomationProperties.AutomationId=\"primary-action\"");
        preview.ElementCorrelations.Should().Contain(item =>
            item.ElementName == "ComposerRoot" && item.JsonPath == "$.layout");
        preview.ElementCorrelations.Should().Contain(item =>
            item.ElementName == "PrimaryAction" && item.JsonPath == "$.layout.slots.children[0]");
    }

    [Fact]
    public void Validation_ShouldRejectInvalidAuthoredIdentitySyntax()
    {
        var blueprint = ValidBlueprint()
            .Replace("\"ComposerRoot\"", "\"not a xaml name\"", StringComparison.Ordinal)
            .Replace("\"composer-root\"", "\" automation id \"", StringComparison.Ordinal);

        var result = new BlueprintValidationService(CreateRegistry()).Validate(blueprint);

        result.Errors.Should().Contain(issue =>
            issue.Code == "InvalidElementName" && issue.JsonPath == "$.layout.elementName");
        result.Errors.Should().Contain(issue =>
            issue.Code == "InvalidAutomationId" && issue.JsonPath == "$.layout.automationId");
    }

    [Fact]
    public void Validation_ShouldRejectDuplicateAuthoredIdentities()
    {
        var duplicate = ValidBlueprint().Replace(
            "\"elementName\": \"PrimaryAction\", \"automationId\": \"primary-action\"",
            "\"elementName\": \"ComposerRoot\", \"automationId\": \"composer-root\"",
            StringComparison.Ordinal);

        var result = new BlueprintValidationService(CreateRegistry()).Validate(duplicate);

        result.Errors.Should().Contain(issue =>
            issue.Code == "DuplicateElementName"
            && issue.JsonPath == "$.layout.slots.children[0].elementName"
            && issue.Message.Contains("$.layout.elementName", StringComparison.Ordinal));
        result.Errors.Should().Contain(issue =>
            issue.Code == "DuplicateAutomationId"
            && issue.JsonPath == "$.layout.slots.children[0].automationId"
            && issue.Message.Contains("$.layout.automationId", StringComparison.Ordinal));
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string ValidBlueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "SemanticIdentity",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "core.stack",
                "elementName": "ComposerRoot",
                "automationId": "composer-root",
                "slots": {
                  "children": [
                    {
                      "kind": "wpfui.button",
                      "elementName": "PrimaryAction", "automationId": "primary-action",
                      "properties": { "text": "Begin proof watch" }
                    }
                  ]
                }
              }
            }
            """;
}
