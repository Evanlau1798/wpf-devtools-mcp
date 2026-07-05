using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.McpTools;

[McpServerToolType]
public static class UiComposerMcpTools
{
    [McpServerTool(Name = "list_ui_block_packs", Title = "List UI Composer Block Packs", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ListUiBlockPacks)]
    public static Task<CallToolResult> ListUiBlockPacks(
        [Description("Optional local WPF project root. When provided, discovers project-local packs from <projectRoot>/.wpfdevtools/packs before user-global and built-in packs.")] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global packs. Omit to use the current user's LocalApplicationData path when available.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ListPacks(projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "get_ui_block_catalog", Title = "Get UI Composer Block Catalog", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.GetUiBlockCatalog)]
    public static Task<CallToolResult> GetUiBlockCatalog(
        [Description("Optional pack IDs to include, such as wpfui. Omit to include all enabled packs.")] string[]? packIds = null,
        [Description("Optional block category filter, such as navigation, input, feedback, window, or layout.")] string? category = null,
        [Description("Optional block kind prefix filter, such as wpfui.navigation.")] string? kindPrefix = null,
        [Description("When true, returns only blocks with an available renderer template.")] bool composableOnly = false,
        [Description("Optional exact block kind for single-block detail, such as wpfui.button.")] string? kind = null,
        [Description("When true, includes recipe catalog entries from the same pack scope in the response.")] bool includeRecipes = false,
        [Description("Optional local WPF project root. When provided, discovers project-local packs from <projectRoot>/.wpfdevtools/packs before user-global and built-in packs.")] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global packs. Omit to use the current user's LocalApplicationData path when available.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("packIds", packIds),
            ("category", category),
            ("kindPrefix", kindPrefix),
            ("composableOnly", composableOnly),
            ("kind", kind),
            ("includeRecipes", includeRecipes),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(GetCatalog(packIds, category, kindPrefix, composableOnly, kind, includeRecipes, projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "validate_ui_blueprint", Title = "Validate UI Composer Blueprint", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ValidateUiBlueprint)]
    public static Task<CallToolResult> ValidateUiBlueprint(
        [Description("UI blueprint JSON text to validate against installed Composer pack contracts.")] string blueprintJson,
        [Description("Optional local WPF project root. When provided, discovers project-local packs from <projectRoot>/.wpfdevtools/packs before user-global and built-in packs.")] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global packs. Omit to use the current user's LocalApplicationData path when available.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ValidateBlueprint(blueprintJson, projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "expand_ui_recipe", Title = "Expand UI Composer Recipe", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ExpandUiRecipe)]
    public static Task<CallToolResult> ExpandUiRecipe(
        [Description("Pack-qualified recipe id, such as wpfui.shellWithNavigation.")] string recipeId,
        [Description("Optional JSON object containing recipe input values. Omit to use recipe defaults.")] System.Text.Json.JsonElement? inputs = null,
        [Description("Optional local WPF project root. When provided, discovers project-local packs from <projectRoot>/.wpfdevtools/packs before user-global and built-in packs.")] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global packs. Omit to use the current user's LocalApplicationData path when available.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("recipeId", recipeId),
            ("inputs", inputs),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ExpandRecipe(recipeId, inputs, projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    private static object ListPacks(string? projectRoot, string? localAppDataRoot)
    {
        var registry = CreateRegistry(projectRoot, localAppDataRoot);
        var result = registry.ListPacks();

        return new
        {
            success = true,
            packCount = result.Packs.Count,
            packs = result.Packs.Select(ToPayload).ToArray(),
            diagnostics = result.Diagnostics
        };
    }

    private static object GetCatalog(
        string[]? packIds,
        string? category,
        string? kindPrefix,
        bool composableOnly,
        string? kind,
        bool includeRecipes,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var registry = CreateRegistry(projectRoot, localAppDataRoot);
        var catalog = new BlockCatalogService(registry);
        var result = catalog.GetCatalog(new BlockCatalogQuery(packIds, category, kindPrefix, composableOnly, kind));
        var recipes = includeRecipes
            ? new RecipeCatalogService(registry).GetCatalog(new RecipeCatalogQuery(packIds)).Items
            : [];

        return new
        {
            success = true,
            itemCount = result.Items.Count,
            items = result.Items,
            recipeCount = recipes.Count,
            recipes,
            diagnostics = result.Diagnostics
        };
    }

    private static object ValidateBlueprint(string blueprintJson, string? projectRoot, string? localAppDataRoot)
    {
        var validator = new BlueprintValidationService(CreateRegistry(projectRoot, localAppDataRoot));
        var result = validator.Validate(blueprintJson);

        return new
        {
            success = true,
            valid = result.Success,
            errorCount = result.Errors.Count,
            warningCount = result.Warnings.Count,
            errors = result.Errors,
            warnings = result.Warnings,
            diagnostics = result.Diagnostics
        };
    }

    private static object ExpandRecipe(
        string recipeId,
        System.Text.Json.JsonElement? inputs,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var result = new RecipeExpansionService(CreateRegistry(projectRoot, localAppDataRoot))
            .Expand(new RecipeExpansionRequest(recipeId, inputs));

        return new
        {
            success = true,
            valid = result.Success,
            result.RecipeId,
            blueprint = result.Blueprint,
            validation = result.Validation,
            errors = result.Errors,
            warnings = result.Warnings,
            diagnostics = result.Diagnostics
        };
    }

    private static PackRegistry CreateRegistry(string? projectRoot, string? localAppDataRoot)
    {
        var composerRoot = ComposerRuntimePaths.ResolveComposerRoot();
        return new PackRegistry(
            ComposerPackPaths.BuiltinRoot(composerRoot),
            ResolveProjectPackRoot(projectRoot),
            ResolveUserPackRoot(localAppDataRoot));
    }

    private static object ToPayload(PackRegistryItem pack)
        => new
        {
            pack.Id,
            pack.Version,
            scope = ToScopeName(pack.Scope),
            pack.BlockCount,
            pack.RecipeCount,
            pack.ExampleCount,
            pack.RendererCount,
            pack.ReadinessValid,
            pack.SourceRepository,
            pack.BlockKinds
        };

    private static string? ResolveProjectPackRoot(string? projectRoot)
        => string.IsNullOrWhiteSpace(projectRoot)
            ? null
            : ComposerPackPaths.ProjectLocalRoot(projectRoot);

    private static string? ResolveUserPackRoot(string? localAppDataRoot)
    {
        var root = string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataRoot;
        return string.IsNullOrWhiteSpace(root)
            ? null
            : ComposerPackPaths.UserGlobalRoot(root);
    }

    private static string ToScopeName(PackScope scope)
        => scope switch
        {
            PackScope.ProjectLocal => "project-local",
            PackScope.UserGlobal => "user-global",
            PackScope.Builtin => "built-in",
            _ => "unknown"
        };
}
