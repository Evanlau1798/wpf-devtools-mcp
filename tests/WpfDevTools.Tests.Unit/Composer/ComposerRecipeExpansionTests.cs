using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRecipeExpansionTests
{
    private static readonly string[] ExpectedStarterRecipes =
    [
        "wpfui.dashboardCards",
        "wpfui.dataGridPage",
        "wpfui.dialogFlow",
        "wpfui.shellWithNavigation",
        "wpfui.tabbedSettings"
    ];

    [Fact]
    public void RecipeCatalog_ShouldExposeStarterRecipesAndInputs()
    {
        var catalog = new RecipeCatalogService(CreateRegistry());

        var result = catalog.GetCatalog(new RecipeCatalogQuery(PackIds: ["wpfui"]));

        result.Items.Select(item => item.Id).Should().BeEquivalentTo(ExpectedStarterRecipes);
        var dataGrid = result.Items.Single(item => item.Id == "wpfui.dataGridPage");
        dataGrid.PackId.Should().Be("wpfui");
        dataGrid.PackVersion.Should().Be("0.1.0");
        dataGrid.RootKind.Should().Be("wpfui.dataGrid");
        dataGrid.Inputs.Keys.Should().Contain(["itemsSource", "emptyText"]);
        dataGrid.RequiredPacks.Should().ContainSingle(pack => pack.Id == "wpfui" && pack.Version == "0.1.0");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUiBlockCatalogTool_ShouldReturnRecipesWhenRequested()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var catalog = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["wpfui"],
                includeRecipes: true,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            var payload = catalog.StructuredContent!.Value;

            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("recipeCount").GetInt32().Should().Be(5);
            payload.GetProperty("recipes").EnumerateArray()
                .Should().Contain(recipe => recipe.GetProperty("id").GetString() == "wpfui.dialogFlow");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RecipeExpansion_ShouldApplyInputDefaultsAndValidateBlueprint()
    {
        var expander = new RecipeExpansionService(CreateRegistry());

        var result = expander.Expand(new RecipeExpansionRequest("wpfui.shellWithNavigation"));

        result.Success.Should().BeTrue();
        result.Validation.Success.Should().BeTrue();
        result.Blueprint.Name.Should().Be("WPF UI Gallery Navigation Shell");
        result.Blueprint.Layout.Kind.Should().Be("wpfui.fluentWindow");
        result.Blueprint.Layout.Slots["titleBar"][0]
            .Properties["title"].GetString().Should().Be("WPF UI Gallery");
    }

    [Fact]
    public void ShellRecipe_ShouldCreateCustomApplicationInformationArchitecture()
    {
        var expander = new RecipeExpansionService(CreateRegistry());
        var inputs = Inputs(
            ("title", "HarborOps Console"),
            ("navigationItem1Text", "Berth Board"),
            ("navigationItem2Text", "Tide Watch"),
            ("navigationItem3Text", "Pilot Roster"),
            ("navigationItem4Text", "Manifest Desk"),
            ("contentHeading", "Live Berth Operations"),
            ("contentBody", "Coordinate pilots, tides, manifests, and active incidents."),
            ("primaryActionText", "Open Incident Log"));

        var result = expander.Expand(new RecipeExpansionRequest("wpfui.shellWithNavigation", inputs));

        result.Success.Should().BeTrue();
        result.Validation.Success.Should().BeTrue();
        var navigation = result.Blueprint.Layout.Slots["content"].Should().ContainSingle().Subject;
        navigation.Slots["items"]
            .Select(item => item.Slots["content"].Single().Properties["value"].GetString())
            .Should().Equal("Berth Board", "Tide Watch", "Pilot Roster", "Manifest Desk");
        var serialized = JsonSerializer.Serialize(result.Blueprint);
        serialized.Should().ContainAll("Live Berth Operations", "Open Incident Log");
        serialized.Should().NotContainAny("All Controls", "Basic input", "NavigationView");
    }

    [Theory]
    [MemberData(nameof(StarterRecipes))]
    public void RecipeExpansion_ShouldValidateEveryStarterRecipe(string recipeId)
    {
        var expander = new RecipeExpansionService(CreateRegistry());

        var result = expander.Expand(new RecipeExpansionRequest(recipeId));

        result.Success.Should().BeTrue();
        result.Validation.Success.Should().BeTrue();
    }

    [Fact]
    public void RecipeExpansion_ShouldSubstituteParametersIntoBlueprintFragment()
    {
        var expander = new RecipeExpansionService(CreateRegistry());
        var inputs = Inputs(
            ("itemsSource", "{Binding Orders}"),
            ("emptyText", "No orders"));

        var result = expander.Expand(new RecipeExpansionRequest("wpfui.dataGridPage", inputs));

        result.Success.Should().BeTrue();
        result.Validation.Success.Should().BeTrue();
        result.Blueprint.Layout.Kind.Should().Be("wpfui.dataGrid");
        result.Blueprint.Layout.Properties["itemsSource"].GetString().Should().Be("{Binding Orders}");
        result.Blueprint.Layout.Slots["emptyState"][0]
            .Properties["text"].GetString().Should().Be("No orders");
    }

    [Fact]
    public void RecipeExpansion_ShouldRejectUnknownRecipe()
    {
        var expander = new RecipeExpansionService(CreateRegistry());

        var result = expander.Expand(new RecipeExpansionRequest("wpfui.missing"));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "RecipeNotFound"
            && issue.JsonPath == "$.recipeId"
            && issue.RepairSuggestion.Contains("get_ui_block_catalog", StringComparison.Ordinal));
    }

    [Fact]
    public void RecipeExpansion_ShouldRejectRecipesReferencingMissingBlocks()
    {
        var projectRoot = CreateTempProjectWithInvalidRecipe();
        try
        {
            var expander = new RecipeExpansionService(CreateRegistry(projectRoot));

            var result = expander.Expand(new RecipeExpansionRequest("broken.badRecipe"));

            result.Success.Should().BeFalse();
            result.Validation.Errors.Should().Contain(issue => issue.Code == "UnknownBlockKind"
                && issue.JsonPath == "$.layout");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task ExpandUiRecipeTool_ShouldReturnStructuredBlueprintAndValidation()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.ExpandUiRecipe(
                "wpfui.dialogFlow",
                inputs: Inputs(("title", "Confirm action")),
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("valid").GetBoolean().Should().BeTrue();
            payload.GetProperty("recipeId").GetString().Should().Be("wpfui.dialogFlow");
            payload.GetProperty("blueprint").GetProperty("layout").GetProperty("kind").GetString()
                .Should().Be("wpfui.contentDialog");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static PackRegistry CreateRegistry(string? projectRoot = null)
    {
        var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
        return new PackRegistry(
            ComposerPackPaths.BuiltinRoot(repoRoot),
            projectRoot is null ? null : ComposerPackPaths.ProjectLocalRoot(projectRoot),
            null);
    }

    private static JsonElement Inputs(params (string Name, string Value)[] values)
    {
        var payload = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return JsonSerializer.SerializeToElement(payload);
    }

    public static TheoryData<string> StarterRecipes()
        => new(ExpectedStarterRecipes);

    private static string CreateTempProjectWithInvalidRecipe()
    {
        var projectRoot = CreateTempDirectory();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "broken", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));

        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"broken","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"broken","displayName":"Broken Pack","version":"1.0.0","blocks":["broken.card"],"recipes":["broken.badRecipe"]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "card.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"broken.card","displayName":"Card","category":"test","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/card.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "recipes", "bad.recipe.json"), """
            {"schemaVersion":"wpfdevtools.ui-recipe.v1","id":"broken.badRecipe","displayName":"Bad Recipe","packId":"broken","requiredPacks":[{"id":"broken","version":"1.0.0","required":true,"role":"primary"}],"expandsTo":{"kind":"broken.missing"}}
            """);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "card.xaml.sbn"), "<TextBlock />");
        return projectRoot;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
