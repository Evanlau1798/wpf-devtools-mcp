using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

[McpServerToolType]
public static partial class UiComposerMcpTools
{
    [McpServerTool(Name = "list_ui_block_packs", Title = "List UI Composer Block Packs", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ListUiBlockPacks)]
    public static Task<CallToolResult> ListUiBlockPacks(
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description(ToolDescriptionFragments.ComposerLocalAppDataRootParameter)] string? localAppDataRoot = null,
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
        [Description("Optional pack IDs to include, such as sample. Omit to include all enabled packs.")] string[]? packIds = null,
        [Description("Optional block category filter, such as navigation, input, feedback, window, or layout.")] string? category = null,
        [Description("Optional block kind prefix filter, such as sample.navigation.")] string? kindPrefix = null,
        [Description("When true, returns only blocks with an available renderer template.")] bool composableOnly = false,
        [Description("Optional exact block kind for single-block detail, such as sample.button.")] string? kind = null,
        [Description("When true, includes recipe catalog entries from the same pack scope in the response.")] bool includeRecipes = false,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description(ToolDescriptionFragments.ComposerLocalAppDataRootParameter)] string? localAppDataRoot = null,
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
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON text or an opaque draftRef to validate against installed Composer pack contracts.")] string blueprintJson,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description(ToolDescriptionFragments.ComposerLocalAppDataRootParameter)] string? localAppDataRoot = null,
        [Description("Optional target XAML file path. Omit to validate against the default Views/<blueprint-name>.xaml target.")] string? targetPath = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("targetPath", targetPath),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ValidateBlueprint(blueprintJson, targetPath, projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "expand_ui_recipe", Title = "Expand UI Composer Recipe", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ExpandUiRecipe)]
    public static Task<CallToolResult> ExpandUiRecipe(
        [Description("Pack-qualified recipe id, such as sample.workspaceStarter.")] string recipeId,
        [Description("Optional JSON object containing recipe input values. Omit to use recipe defaults.")] System.Text.Json.JsonElement? inputs = null,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description(ToolDescriptionFragments.ComposerLocalAppDataRootParameter)] string? localAppDataRoot = null,
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

    [McpServerTool(Name = "render_ui_blueprint", Title = "Render UI Composer Blueprint Dry Run", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.RenderUiBlueprint)]
    public static Task<CallToolResult> RenderUiBlueprint(
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON text or an opaque draftRef to render in dry-run mode.")] string blueprintJson,
        [Description("Optional target XAML file path suggestion. The renderer does not write this file.")] string? targetPath = null,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description(ToolDescriptionFragments.ComposerLocalAppDataRootParameter)] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("targetPath", targetPath),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(RenderBlueprint(blueprintJson, targetPath, projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "repair_ui_blueprint", Title = "Repair UI Composer Blueprint", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.RepairUiBlueprint)]
    public static Task<CallToolResult> RepairUiBlueprint(
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON text or an opaque draftRef to analyze for repair guidance.")] string blueprintJson,
        [Description("Optional preview, renderer, or compile diagnostics JSON object or array returned by preview_ui_blueprint or render_ui_blueprint.")] string? diagnosticsJson = null,
        [Description("Optional target XAML file path suggestion used only for render diagnostics. This tool does not write the file.")] string? targetPath = null,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description(ToolDescriptionFragments.ComposerLocalAppDataRootParameter)] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("diagnosticsJson", diagnosticsJson),
            ("targetPath", targetPath),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(RepairBlueprint(blueprintJson, diagnosticsJson, targetPath, projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "apply_ui_blueprint", Title = "Apply UI Composer Blueprint", OpenWorld = false, ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ApplyUiBlueprint)]
    public static Task<CallToolResult> ApplyUiBlueprint(
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON text or an opaque draftRef to apply.")] string blueprintJson,
        [Description("Local WPF project root used for file planning and write allowlist checks.")] string projectRoot,
        [Description("Optional project-root-relative target XAML file path. Defaults to Views/<blueprint name>.xaml. Absolute paths are rejected.")] string? targetPath = null,
        [Description("When true or omitted, returns a dry-run plan without writing files.")] bool dryRun = true,
        [Description("Required explicit confirmation for non-dry-run writes after reviewing the dry-run file plan.")] bool confirmApply = false,
        [Description("Optional LocalApplicationData root override for user-global packs.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("projectRoot", projectRoot),
            ("targetPath", targetPath),
            ("dryRun", dryRun),
            ("confirmApply", confirmApply),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ApplyBlueprint(blueprintJson, projectRoot, targetPath, dryRun, confirmApply, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "preview_ui_blueprint", Title = "Preview UI Composer Blueprint Compile Smoke", OpenWorld = false, ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.PreviewUiBlueprint)]
    public static Task<CallToolResult> PreviewUiBlueprint(
        SessionManager sessionManager,
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON text or an opaque draftRef to compile in a temporary preview project.")] string blueprintJson,
        [Description("When true, runs dotnet restore for the temporary preview project before build. When false, build runs with --no-restore and reports missing assets diagnostics.")] bool restoreEnabled = true,
        [Description("When true, starts the temporary preview host after a successful build, waits for its main window, then terminates it.")] bool startHost = false,
        [Description("When true with startHost=true, connects to the temporary preview host and returns semantic summary plus layout diagnostics. Requires the sensitive-reads policy gate.")] bool includeRuntimeDiagnostics = false,
        [Description("When true with startHost=true, enables runtime diagnostics and also requests screenshot diagnostics. Requires the sensitive-reads and screenshot policy gates.")] bool includeScreenshotDiagnostics = false,
        [AllowedValues("metadata", "file")]
        [Description("Screenshot output mode used when includeScreenshotDiagnostics=true: 'metadata' (default) or resource-backed 'file'.")] string screenshotOutputMode = "metadata",
        [Range(1, int.MaxValue)]
        [Description("Optional maximum preview screenshot width. Defaults to 1024 for reliable agent image consumption; pass null for the rendered width.")] int? screenshotMaxWidth = 1024,
        [Range(1, int.MaxValue)]
        [Description("Optional maximum preview screenshot height. Defaults to 1024 for reliable agent image consumption; pass null for the rendered height.")] int? screenshotMaxHeight = 1024,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global packs.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("restoreEnabled", restoreEnabled),
            ("startHost", startHost),
            ("includeRuntimeDiagnostics", includeRuntimeDiagnostics),
            ("includeScreenshotDiagnostics", includeScreenshotDiagnostics),
            ("screenshotOutputMode", screenshotOutputMode),
            ("screenshotMaxWidth", screenshotMaxWidth),
            ("screenshotMaxHeight", screenshotMaxHeight),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, token) => PreviewBlueprint(
                sessionManager,
                blueprintJson,
                restoreEnabled,
                startHost,
                includeRuntimeDiagnostics,
                includeScreenshotDiagnostics,
                screenshotOutputMode,
                screenshotMaxWidth,
                screenshotMaxHeight,
                projectRoot,
                localAppDataRoot,
                token),
            args,
            cancellationToken,
            timeoutSeconds: 135);
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
            allowedPackRoles = ComposerPackRoles.All.Order(StringComparer.Ordinal).ToArray(),
            diagnostics = result.Diagnostics,
            observability = ComposerObservability.ForPackList(result.Diagnostics)
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
            authoringGuidance = new
            {
                strategy = "brief-first",
                recipesRequested = includeRecipes,
                creativeBriefRequired = true,
                principles = new[]
                {
                    "Choose an original product purpose and information architecture from discovered pack capabilities before selecting a recipe.",
                    "Treat recipes as optional accelerators or fragments, then adapt them to the independent creative brief.",
                    "Use pack-defined descriptions, customization guidance, skeletons, and slot rules instead of assuming a library-specific design."
                }
            },
            diagnostics = result.Diagnostics,
            observability = ComposerObservability.ForCatalog(result.Diagnostics)
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
            diagnostics = result.Diagnostics,
            observability = ComposerObservability.ForRecipeExpansion(result)
        };
    }

    private static object RenderBlueprint(
        string blueprintJson,
        string? targetPath,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var result = new UiBlueprintRenderer(CreateRegistry(projectRoot, localAppDataRoot))
            .Render(new RenderBlueprintRequest(input.BlueprintJson, targetPath, projectRoot));

        return new
        {
            success = true,
            valid = result.Valid,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            result.DryRun,
            result.Xaml,
            result.FilePlan,
            result.RequiredResources,
            result.RequiredNuGetPackages,
            result.PackageIntegrationGuidance,
            validation = result.Validation,
            errors = result.Errors,
            diagnostics = result.Diagnostics,
            observability = ComposerObservability.ForRenderDryRun(result)
        };
    }

    private static object RepairBlueprint(
        string blueprintJson,
        string? diagnosticsJson,
        string? targetPath,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var result = new BlueprintRepairService(CreateRegistry(projectRoot, localAppDataRoot))
            .Repair(new BlueprintRepairRequest(input.BlueprintJson, diagnosticsJson, targetPath));

        return new
        {
            result.Success,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            result.Repairable,
            result.GeneratedXamlPatch,
            result.ActionCount,
            result.Actions,
            result.Diagnostics,
            observability = ComposerObservability.ForRepair(result)
        };
    }

    private static object ApplyBlueprint(
        string blueprintJson,
        string projectRoot,
        string? targetPath,
        bool dryRun,
        bool confirmApply,
        string? localAppDataRoot)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var result = new UiBlueprintApplyService(CreateRegistry(projectRoot, localAppDataRoot))
            .Apply(new ApplyBlueprintRequest(input.BlueprintJson, projectRoot, targetPath, dryRun, confirmApply));

        return new
        {
            result.Success,
            result.Valid,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            result.DryRun,
            result.RequiresConfirmation,
            result.WouldWriteFiles,
            result.Xaml,
            result.FilePlan,
            result.ResourcePlan,
            result.RequiredNuGetPackages,
            packageIntegrationGuidance = PackageIntegrationPlanner.Create(projectRoot, result.RequiredNuGetPackages),
            result.ViewModelBindingContract,
            result.BehaviorIntegrationContract,
            result.ProjectIntegrationPlan,
            result.Errors,
            observability = ComposerObservability.ForApply(result)
        };
    }

}
