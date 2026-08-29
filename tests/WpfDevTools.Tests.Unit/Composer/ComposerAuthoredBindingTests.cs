using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerAuthoredBindingTests
{
    [Fact]
    public void Renderer_ShouldMaterializeAuthoredBindingsOnTheBlockRoot()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

        var result = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest(Blueprint()));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().Contain("Text=\"{Binding StatusMessage}\"");
        result.Xaml.Should().Contain("Tag=\"{Binding SelectedItem, Mode=TwoWay}\"");
        result.Xaml.Should().NotContain("Text=\"Fallback\"");
    }

    [Fact]
    public void Validation_ShouldRejectUnsafeBindingNamesAndNonBindingValues()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var blueprint = Blueprint()
            .Replace("\"Text\": \"{Binding StatusMessage}\"", "\"x:Name\": \"{Binding StatusMessage}\"", StringComparison.Ordinal)
            .Replace("\"Tag\": \"{Binding SelectedItem, Mode=TwoWay}\"", "\"Tag\": \"SelectedItem\"", StringComparison.Ordinal);

        var result = new BlueprintValidationService(registry).Validate(blueprint);

        result.Errors.Should().Contain(issue =>
            issue.Code == "InvalidBindingPropertyName" && issue.JsonPath.EndsWith(".bindings.x:Name", StringComparison.Ordinal));
        result.Errors.Should().Contain(issue =>
            issue.Code == "BindingExpressionInvalid" && issue.JsonPath.EndsWith(".bindings.Tag", StringComparison.Ordinal));
    }

    [Fact]
    public void BindingContract_ShouldIncludeAuthoredBindingMapEntries()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

        var requirements = ViewModelBindingRequirementBuilder.Build(registry, Blueprint());

        requirements.Should().Contain(requirement =>
            requirement.BindingPath == "StatusMessage"
            && requirement.Usages.Any(usage =>
                usage.JsonPath == "$.layout.bindings.Text"
                && usage.PropertyName == "Text"));
    }

    private static string Blueprint() =>
        """
        {
          "schemaVersion": "wpfdevtools.ui-blueprint.v1",
          "name": "AuthoredBindings",
          "packs": [{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }],
          "primaryPack": "core",
          "layout": {
            "kind": "core.text",
            "properties": { "text": "Fallback" },
            "bindings": {
              "Text": "{Binding StatusMessage}",
              "Tag": "{Binding SelectedItem, Mode=TwoWay}"
            }
          }
        }
        """;
}
