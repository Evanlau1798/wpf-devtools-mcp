using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    [McpServerTool(Name = "apply_ui_project_integration", Title = "Apply Reviewed UI Composer Project Integration", OpenWorld = false, ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ApplyUiProjectIntegration)]
    public static Task<CallToolResult> ApplyUiProjectIntegration(
        [StringLength(BoundaryStringLimits.MaxStringifiedJsonArgumentLength)]
        [Description("UI blueprint JSON text or opaque draftRef used to regenerate the reviewed project integration plan.")] string blueprintJson,
        [Description("Exact allowlisted local WPF project root from the reviewed dry-run.")] string projectRoot,
        [Description("Exact projectIntegrationPlan.planHash returned by the latest apply_ui_blueprint dry-run.")] string reviewedPlanHash,
        [Description("Optional project-root-relative target XAML path used by the reviewed dry-run.")] string? targetPath = null,
        [Description("Required explicit confirmation that only the exact reviewed integration plan may be applied.")] bool confirmIntegration = false,
        [Description("Optional LocalApplicationData root override for user-global packs.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("blueprintJson", blueprintJson),
            ("projectRoot", projectRoot),
            ("reviewedPlanHash", reviewedPlanHash),
            ("targetPath", targetPath),
            ("confirmIntegration", confirmIntegration),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ApplyProjectIntegration(
                blueprintJson,
                projectRoot,
                reviewedPlanHash,
                targetPath,
                confirmIntegration,
                localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 30);
    }

    private static object ApplyProjectIntegration(
        string blueprintJson,
        string projectRoot,
        string reviewedPlanHash,
        string? targetPath,
        bool confirmIntegration,
        string? localAppDataRoot)
    {
        var input = BlueprintInputResolver.Resolve(blueprintJson);
        if (!input.Success)
        {
            return BlueprintDraftError(input.Error!);
        }

        var result = new UiBlueprintProjectIntegrationService(CreateRegistry(projectRoot, localAppDataRoot))
            .Apply(new ProjectIntegrationRequest(
                input.BlueprintJson,
                projectRoot,
                targetPath,
                reviewedPlanHash,
                confirmIntegration));
        var packageRestoreRequired = result.Applied && result.Changes.Any(change =>
            change.Role is "package-reference" or "central-package-version");

        return new
        {
            result.Success,
            blueprintDraftRef = input.IsDraft ? input.DraftRef : null,
            result.Applied,
            result.RolledBack,
            result.PlanHash,
            result.Changes,
            packageRestoreRequired,
            buildGuidance = packageRestoreRequired
                ? "Package references changed. Run dotnet restore for the updated project before dotnet build --no-restore."
                : null,
            result.Errors
        };
    }
}
