using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerStackSpacingDiagnosticsTests
{
    [Fact]
    public void AddIssues_ShouldWarnWhenLargeLeadingMarginActsAsHorizontalStackSpacer()
    {
        var child = Node("core.border", ("margin", "300,0,0,0"));
        var root = Node("core.stack", ("orientation", "Horizontal"));
        root.Slots["children"] = [Node("core.border"), child];
        var warnings = new List<BlueprintValidationIssue>();

        BlueprintStackSpacingDiagnostics.AddIssues(root, "$.layout", warnings);

        var warning = warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be("LargeFixedStackSpacing");
        warning.JsonPath.Should().Be("$.layout.slots.children[1].properties.margin");
        warning.RepairSuggestion.Should().Contain("core.grid");
    }

    [Theory]
    [InlineData("Horizontal", "48,0,0,0")]
    [InlineData("Vertical", "0,72,0,0")]
    [InlineData("Horizontal", "0,140,0,0")]
    public void AddIssues_ShouldIgnoreLocalOrCrossAxisSpacing(string orientation, string margin)
    {
        var child = Node("core.border", ("margin", margin));
        var root = Node("core.stack", ("orientation", orientation));
        root.Slots["children"] = [Node("core.border"), child];
        var warnings = new List<BlueprintValidationIssue>();

        BlueprintStackSpacingDiagnostics.AddIssues(root, "$.layout", warnings);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void AddIssues_ShouldIgnoreFirstChildInset()
    {
        var root = Node("core.stack", ("orientation", "Horizontal"));
        root.Slots["children"] =
        [
            Node("core.border", ("margin", 140)),
            Node("core.border")
        ];
        var warnings = new List<BlueprintValidationIssue>();

        BlueprintStackSpacingDiagnostics.AddIssues(root, "$.layout", warnings);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldSurfaceLargeSiblingSpacingAdvisory()
    {
        var validator = new BlueprintValidationService(
            PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath(".")));

        var result = validator.Validate("""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "SpacingAdvisory",
              "packs": [{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "core",
              "layout": {
                "kind": "core.stack",
                "properties": { "orientation": "Horizontal" },
                "slots": { "children": [
                  { "kind": "core.border" },
                  { "kind": "core.border", "properties": { "margin": 120 } }
                ] }
              }
            }
            """);

        result.Success.Should().BeTrue(
            "validation errors: {0}",
            string.Join("; ", result.Errors.Select(issue => $"{issue.Code}@{issue.JsonPath}")));
        result.Warnings.Should().ContainSingle(issue => issue.Code == "LargeFixedStackSpacing")
            .Which.JsonPath.Should().Be("$.layout.slots.children[1].properties.margin");
    }

    private static UiBlueprintNode Node(string kind, params (string Name, object Value)[] properties)
        => new()
        {
            Kind = kind,
            Properties = properties.ToDictionary(
                item => item.Name,
                item => JsonSerializer.SerializeToElement(item.Value),
                StringComparer.Ordinal)
        };
}
