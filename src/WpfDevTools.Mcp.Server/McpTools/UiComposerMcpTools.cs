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
            (_, token) => Task.FromResult<object>(ListPacks(projectRoot, localAppDataRoot, token)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "get_ui_block_catalog", Title = "Get UI Composer Block Catalog", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.GetUiBlockCatalog)]
    public static Task<CallToolResult> GetUiBlockCatalog(
        [Description("Pack IDs to include; omit for all enabled packs.")] string[]? packIds = null,
        [Description("Optional exact block category.")] string? category = null,
        [Description("Optional pack-qualified block kind prefix.")] string? kindPrefix = null,
        [Description("Return only blocks with renderer templates.")] bool composableOnly = false,
        [Description("Optional exact pack-qualified block kind.")] string? kind = null,
        [Description("Include recipes from the same pack scope.")] bool includeRecipes = false,
        [Description("Return brief descriptions, property names/warnings, slot bounds, skeletons, and roles; use false with exact kind for full contracts.")] bool compact = false,
        [StringLength(128)]
        [Description("Case-insensitive allowed-value substring search; use with exact kind. Max 128 characters.")] string? allowedValueQuery = null,
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
            ("compact", compact),
            ("allowedValueQuery", allowedValueQuery),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(GetCatalog(packIds, category, kindPrefix, composableOnly, kind, includeRecipes, compact, allowedValueQuery, projectRoot, localAppDataRoot)),
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
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Target Window.Width in DIPs; copy preview viewportWidth.")] int? targetWindowWidth = null,
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Target Window.Height in DIPs; copy preview viewportHeight.")] int? targetWindowHeight = null,
        [Description("Optional LocalApplicationData root override for user-global packs.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("projectRoot", projectRoot),
            ("targetPath", targetPath),
            ("dryRun", dryRun),
            ("confirmApply", confirmApply),
            ("targetWindowWidth", targetWindowWidth),
            ("targetWindowHeight", targetWindowHeight),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ApplyBlueprint(blueprintJson, projectRoot, targetPath, dryRun, confirmApply, targetWindowWidth, targetWindowHeight, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "preview_ui_blueprint", Title = "Preview UI Composer Blueprint Compile Smoke", OpenWorld = false, ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.PreviewUiBlueprint)]
    public static Task<CallToolResult> PreviewUiBlueprint(
        SessionManager sessionManager,
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON text or an opaque draftRef to compile in a temporary preview project.")] string blueprintJson,
        [Description("Restore before build; false uses --no-restore and reports missing assets.")] bool restoreEnabled = true,
        [Description("When true, starts the temporary preview host after a successful build, waits for its main window, then terminates it.")] bool startHost = false,
        [Description("With startHost, returns semantic and layout diagnostics; requires sensitive reads.")] bool includeRuntimeDiagnostics = false,
        [Description("With startHost, adds a screenshot; requires sensitive-read and screenshot gates.")] bool includeScreenshotDiagnostics = false,
        [AllowedValues("metadata", "file")]
        [Description("Screenshot output mode used when includeScreenshotDiagnostics=true: 'metadata' (default) or resource-backed 'file'.")] string screenshotOutputMode = "metadata",
        [Range(1, int.MaxValue)]
        [Description("Optional maximum preview screenshot width. Defaults to 1024 for reliable agent image consumption; pass null for the rendered width.")] int? screenshotMaxWidth = 1024,
        [Range(1, int.MaxValue)]
        [Description("Optional maximum preview screenshot height. Defaults to 1024 for reliable agent image consumption; pass null for the rendered height.")] int? screenshotMaxHeight = 1024,
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Preview Window.Width in DIPs; match the target Window dimension to expose overflow.")] int? viewportWidth = null,
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Preview Window.Height in DIPs; match the target Window dimension to expose overflow.")] int? viewportHeight = null,
        [MaxLength(UiPreviewRuntimeDependencyPolicy.MaximumCallApprovalTokens)]
        [Description("Reviewed one-call content tokens; requires WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS=true.")] string[]? runtimePackApprovalTokens = null,
        [Range(1, UiBlueprintPreviewDiagnosticsBridge.MaximumNameLookupLimit)]
        [Description("Inspects up to 32 non-generated correlation names (authored elementName values and renderer-provided root x:Name values); raise to 64 only for layoutRiskSummary lookup-budget truncation.")] int correlationLookupLimit = UiBlueprintPreviewDiagnosticsBridge.ExistingNameLookupLimit,
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
            ("viewportWidth", viewportWidth),
            ("viewportHeight", viewportHeight),
            ("runtimePackApprovalTokens", runtimePackApprovalTokens),
            ("correlationLookupLimit", correlationLookupLimit),
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
                viewportWidth,
                viewportHeight,
                runtimePackApprovalTokens,
                correlationLookupLimit,
                projectRoot,
                localAppDataRoot,
                token),
            args,
            cancellationToken,
            timeoutSeconds: 135);
    }

    private static object ListPacks(
        string? projectRoot,
        string? localAppDataRoot,
        CancellationToken cancellationToken)
    {
        var registry = CreateRegistry(projectRoot, localAppDataRoot);
        var result = registry.ListPacks(cancellationToken);

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
        bool compact,
        string? allowedValueQuery,
        string? projectRoot,
        string? localAppDataRoot)
    {
        var registry = CreateRegistry(projectRoot, localAppDataRoot);
        var catalog = new BlockCatalogService(registry);
        var result = catalog.GetCatalog(new BlockCatalogQuery(packIds, category, kindPrefix, composableOnly, kind, allowedValueQuery));
        var recipes = includeRecipes
            ? new RecipeCatalogService(registry).GetCatalog(new RecipeCatalogQuery(packIds)).Items
            : [];
        object items = compact
            ? result.Items.Select(ToCompactCatalogItem).ToArray()
            : result.Items;
        return new
        {
            success = true,
            compact,
            itemCount = result.Items.Count,
            items,
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

    private static object ToCompactCatalogItem(BlockCatalogItem item)
        => new
        {
            item.PackId,
            item.PackVersion,
            item.Kind,
            item.DisplayName,
            item.Description,
            item.Category,
            propertyNames = item.Properties.Keys.Order(StringComparer.Ordinal).ToArray(),
            propertyWarnings = item.Properties
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.PreviewWarning))
                .ToDictionary(pair => pair.Key, pair => pair.Value.PreviewWarning, StringComparer.Ordinal),
            slots = item.Slots.ToDictionary(
                pair => pair.Key,
                pair => new
                {
                    pair.Value.AllowedKinds,
                    pair.Value.MinItems,
                    pair.Value.MaxItems
                },
                StringComparer.Ordinal),
            item.RendererAvailable,
            item.CompositionSkeleton,
            item.AuthoringRoles
        };

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
        int? targetWindowWidth,
        int? targetWindowHeight,
        string? localAppDataRoot)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var result = new UiBlueprintApplyService(CreateRegistry(projectRoot, localAppDataRoot))
            .Apply(new ApplyBlueprintRequest(input.BlueprintJson, projectRoot, targetPath, dryRun, confirmApply, targetWindowWidth, targetWindowHeight));

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
            result.TargetWindowPlan,
            result.ProjectIntegrationPlan,
            result.Errors,
            observability = ComposerObservability.ForApply(result)
        };
    }

}
