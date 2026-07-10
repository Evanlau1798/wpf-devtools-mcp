using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBehaviorIntegrationContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ShellRecipe_ShouldRenderBindableNavigationAndPrimaryActionCommands()
    {
        var registry = CreateRegistry();
        var recipe = new RecipeExpansionService(registry)
            .Expand(new RecipeExpansionRequest("wpfui.shellWithNavigation"));

        var render = new UiBlueprintRenderer(registry).Render(
            new RenderBlueprintRequest(JsonSerializer.Serialize(recipe.Blueprint, JsonOptions)));

        render.Success.Should().BeTrue();
        render.Xaml.Should().Contain("Command=\"{Binding NavigateCommand}\"");
        render.Xaml.Should().Contain("CommandParameter=\"workspace\"");
        render.Xaml.Should().Contain("Command=\"{Binding PrimaryActionCommand}\"");
    }

    [Fact]
    public void ApplyBlueprint_ShouldDescribeEveryShellInteractionInMachineReadableContract()
    {
        var registry = CreateRegistry();
        var recipe = new RecipeExpansionService(registry)
            .Expand(new RecipeExpansionRequest("wpfui.shellWithNavigation"));
        var blueprintJson = JsonSerializer.Serialize(recipe.Blueprint, JsonOptions);

        var result = new UiBlueprintApplyService(registry).Apply(
            new ApplyBlueprintRequest(blueprintJson, Path.Combine(Path.GetTempPath(), "composer-behavior-contract")));

        result.BehaviorIntegrationContract.Status.Should().Be("required");
        result.BehaviorIntegrationContract.Interactions.Should().HaveCount(6);
        using var document = JsonDocument.Parse(result.ViewModelBindingContract.Content);
        var behavior = document.RootElement.GetProperty("behaviorIntegration");
        behavior.GetProperty("status").GetString().Should().Be("required");
        behavior.GetProperty("sourceRecipeId").GetString().Should().Be("wpfui.shellWithNavigation");
        var interactions = behavior.GetProperty("interactions").EnumerateArray().ToArray();
        interactions.Should().HaveCount(6);
        interactions.Count(item => item.GetProperty("kind").GetString() == "navigation").Should().Be(5);
        interactions.Should().Contain(item =>
            item.GetProperty("commandPath").GetString() == "NavigateCommand"
            && item.GetProperty("commandParameter").GetString() == "workspace");
        interactions.Should().ContainSingle(item =>
            item.GetProperty("kind").GetString() == "action"
            && item.GetProperty("commandPath").GetString() == "PrimaryActionCommand");
        behavior.GetProperty("verificationGuidance").GetString().Should().Contain("state or visible content change");
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
}
