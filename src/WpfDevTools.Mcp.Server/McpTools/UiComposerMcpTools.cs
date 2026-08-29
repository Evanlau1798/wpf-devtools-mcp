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
        [Description("Pack IDs; omit for all enabled packs.")] string[]? packIds = null,
        [Description("Exact block category.")] string? category = null,
        [Description("Exact block authoring role; not pack role.")] string? authoringRole = null,
        [Description("Pack-qualified kind prefix.")] string? kindPrefix = null,
        [Description("Only blocks with renderer templates.")] bool composableOnly = false,
        [Description("Exact pack-qualified block kind.")] string? kind = null,
        [Description("Include recipes from the same pack scope.")] bool includeRecipes = false,
        [Description("Compact discovery with required/bounded property contracts; false returns full exact-kind details.")] bool compact = false,
        [StringLength(128)]
        [Description("Case-insensitive allowed-value substring search; use with exact kind. Max 128 characters.")] string? allowedValueQuery = null,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description(ToolDescriptionFragments.ComposerLocalAppDataRootParameter)] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("packIds", packIds),
            ("category", category),
            ("authoringRole", authoringRole),
            ("kindPrefix", kindPrefix),
            ("composableOnly", composableOnly),
            ("kind", kind),
            ("includeRecipes", includeRecipes),
            ("compact", compact),
            ("allowedValueQuery", allowedValueQuery),
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(GetCatalog(packIds, category, authoringRole, kindPrefix, composableOnly, kind, includeRecipes, compact, allowedValueQuery, projectRoot, localAppDataRoot)),
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
        [Description("Return full XAML; false by default. Use render_ui_blueprint for review.")] bool includeGeneratedXaml = false,
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Target Window.Width in DIPs; copy preview viewportWidth.")] int? targetWindowWidth = null,
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Target Window.Height in DIPs; copy preview viewportHeight.")] int? targetWindowHeight = null,
        [Description("Optional LocalApplicationData root override for user-global packs.")] string? localAppDataRoot = null,
        ModelContextProtocol.Server.McpServer? server = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("projectRoot", projectRoot),
            ("targetPath", targetPath),
            ("dryRun", dryRun),
            ("confirmApply", confirmApply),
            ("includeGeneratedXaml", includeGeneratedXaml),
            ("targetWindowWidth", targetWindowWidth),
            ("targetWindowHeight", targetWindowHeight),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ApplyBlueprint(blueprintJson, projectRoot, targetPath, dryRun, confirmApply, includeGeneratedXaml, targetWindowWidth, targetWindowHeight, localAppDataRoot, CreateProjectWriteAuthorizer(server))),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    [McpServerTool(Name = "preview_ui_blueprint", Title = "Preview UI Composer Blueprint Compile Smoke", OpenWorld = false, ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.PreviewUiBlueprint)]
    public static Task<CallToolResult> PreviewUiBlueprint(
        SessionManager sessionManager,
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("Blueprint JSON or draftRef for isolated preview compile.")] string blueprintJson,
        [Description("Restore before build; false uses --no-restore.")] bool restoreEnabled = true,
        [Description("Launch the temporary preview host after build.")] bool startHost = false,
        [Description("Host semantic/layout diagnostics; needs startHost and sensitive reads.")] bool includeRuntimeDiagnostics = false,
        [Description("Compact successful XAML/correlations; failures and screenshot handles remain.")] bool compactRuntimeDiagnostics = true,
        [Description("Host screenshot; needs startHost, sensitive-read, and screenshot gates.")] bool includeScreenshotDiagnostics = false,
        [AllowedValues("metadata", "file")]
        [Description("Screenshot mode: metadata or resource-backed file.")] string screenshotOutputMode = "metadata",
        [Range(1, int.MaxValue)]
        [Description("Maximum screenshot width; defaults to 1024. Null keeps rendered width.")] int? screenshotMaxWidth = 1024,
        [Range(1, int.MaxValue)]
        [Description("Maximum screenshot height; defaults to 1024. Null keeps rendered height.")] int? screenshotMaxHeight = 1024,
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Preview Window.Width DIPs for target overflow.")] int? viewportWidth = null,
        [Range(1, UiPreviewProjectFiles.MaximumViewportDimension)]
        [Description("Preview Window.Height DIPs for target overflow.")] int? viewportHeight = null,
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("Pack-neutral named-region geometry JSON; requires startHost.")] string? visualLayoutContractJson = null,
        [Description("One-request reviewed tokens; requires WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS=true.")] string[]? runtimePackApprovalTokens = null,
        [Range(1, UiBlueprintPreviewDiagnosticsBridge.MaximumNameLookupLimit)]
        [Description("Up to 32 non-generated correlation names (authored elementName values and renderer-provided root x:Name values); raise to 64 after truncation. Contract names have separate priority.")] int correlationLookupLimit = UiBlueprintPreviewDiagnosticsBridge.ExistingNameLookupLimit,
        [Description(ToolDescriptionFragments.ComposerProjectRootParameter)] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global packs.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("restoreEnabled", restoreEnabled),
            ("startHost", startHost),
            ("includeRuntimeDiagnostics", includeRuntimeDiagnostics),
            ("compactRuntimeDiagnostics", compactRuntimeDiagnostics),
            ("includeScreenshotDiagnostics", includeScreenshotDiagnostics),
            ("screenshotOutputMode", screenshotOutputMode),
            ("screenshotMaxWidth", screenshotMaxWidth),
            ("screenshotMaxHeight", screenshotMaxHeight),
            ("viewportWidth", viewportWidth),
            ("viewportHeight", viewportHeight),
            ("visualLayoutContractJson", visualLayoutContractJson),
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
                compactRuntimeDiagnostics,
                includeScreenshotDiagnostics,
                screenshotOutputMode,
                screenshotMaxWidth,
                screenshotMaxHeight,
                viewportWidth,
                viewportHeight,
                visualLayoutContractJson,
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
        string? authoringRole,
        string? kindPrefix,
        bool composableOnly,
        string? kind,
        bool includeRecipes,
        bool compact,
        string? allowedValueQuery,
        string? projectRoot,
        string? localAppDataRoot)
    {
        if (!string.IsNullOrWhiteSpace(allowedValueQuery) && string.IsNullOrWhiteSpace(kind))
        {
            return new
            {
                success = false,
                errorCode = "CatalogExactKindRequired",
                error = "allowedValueQuery requires an exact pack-qualified kind.",
                hint = "Pass kind from broad discovery, or omit allowedValueQuery for broad discovery."
            };
        }

        var registry = CreateRegistry(projectRoot, localAppDataRoot);
        var catalog = new BlockCatalogService(registry);
        var query = new BlockCatalogQuery(packIds, category, kindPrefix, composableOnly, kind, allowedValueQuery, authoringRole);
        var result = catalog.GetCatalog(query);
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
            authoringRoleResolution = BuildAuthoringRoleResolution(catalog, query, authoringRole, result.Items.Count > 0),
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
        bool includeGeneratedXaml,
        int? targetWindowWidth,
        int? targetWindowHeight,
        string? localAppDataRoot,
        Func<string, ProjectWriteAuthorization>? authorizeProjectWrite)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var result = new UiBlueprintApplyService(
                CreateRegistry(projectRoot, localAppDataRoot),
                authorizeProjectWrite)
            .Apply(new ApplyBlueprintRequest(input.BlueprintJson, projectRoot, targetPath, dryRun, confirmApply, targetWindowWidth, targetWindowHeight));

        return new
        {
            result.Success,
            result.Valid,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            result.DryRun,
            result.RequiresConfirmation,
            result.WouldWriteFiles,
            xaml = includeGeneratedXaml ? result.Xaml : null,
            generatedXamlOmitted = !includeGeneratedXaml && result.Xaml.Length > 0,
            generatedXamlLength = result.Xaml?.Length ?? 0,
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
