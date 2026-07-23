using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    [McpServerTool(Name = "import_ui_block_pack", Title = "Import UI Composer Block Pack", OpenWorld = false, ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ImportUiBlockPack)]
    public static Task<CallToolResult> ImportUiBlockPack(
        [StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
        [Description("Absolute local normalized pack archive path.")] string archivePath,
        [StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
        [Description("Absolute local project root; import stays under .wpfdevtools/packs.")] string projectRoot,
        [Description("Return a plan without writing. Defaults to true.")] bool dryRun = true,
        [Description("Required when dryRun=false.")] bool confirmImport = false,
        [Description("SHA-256 returned by the reviewed dry run; required when dryRun=false.")] string? reviewedArchiveSha256 = null,
        [Description("Allow same-version replacement. Defaults to false.")] bool allowOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("archivePath", archivePath),
            ("projectRoot", projectRoot),
            ("dryRun", dryRun),
            ("confirmImport", confirmImport),
            ("reviewedArchiveSha256", reviewedArchiveSha256),
            ("allowOverwrite", allowOverwrite));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, token) => ImportPackAsync(
                archivePath,
                projectRoot,
                dryRun,
                confirmImport,
                reviewedArchiveSha256,
                allowOverwrite,
                token),
            args,
            cancellationToken,
            timeoutSeconds: 30);
    }

    private static async Task<object> ImportPackAsync(
        string archivePath,
        string projectRoot,
        bool dryRun,
        bool confirmImport,
        string? reviewedArchiveSha256,
        bool allowOverwrite,
        CancellationToken cancellationToken)
    {
        var normalizedProjectRoot = NormalizeImportProjectRoot(projectRoot);
        var normalizedArchivePath = NormalizeArchivePath(archivePath);
        var destinationRoot = ComposerPackPaths.ProjectLocalRoot(normalizedProjectRoot);
        var plan = await PackImportService.CreateDryRunPlanAsync(
                normalizedArchivePath,
                destinationRoot,
                limits: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (dryRun)
        {
            return ToImportPayload(plan, destinationRoot, imported: false, requiresConfirmation: true);
        }

        if (!confirmImport)
        {
            return ImportFailure(
                "ImportConfirmationRequired",
                "$.confirmImport",
                "Non-dry-run pack import requires confirmImport=true.",
                "Review the dry-run file plan, then retry with confirmImport=true for the same archive and projectRoot.",
                requiresConfirmation: true);
        }

        if (string.IsNullOrWhiteSpace(reviewedArchiveSha256))
        {
            return ImportFailure(
                "ReviewedArchiveHashRequired",
                "$.reviewedArchiveSha256",
                "Non-dry-run pack import requires the SHA-256 returned by the reviewed dry run.",
                "Run a dry import, review its file plan, then pass archiveSha256 as reviewedArchiveSha256.",
                requiresConfirmation: true);
        }

        if (reviewedArchiveSha256.Length != 64 || reviewedArchiveSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            return ImportFailure(
                "InvalidReviewedArchiveHash",
                "$.reviewedArchiveSha256",
                "reviewedArchiveSha256 must be a 64-character SHA-256 hexadecimal value.",
                "Copy archiveSha256 exactly from the dry-run response.",
                requiresConfirmation: true);
        }

        if (!string.Equals(plan.ArchiveSha256, reviewedArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            return ImportPlanChangedFailure();
        }

        var authorization = ProjectWritePolicy.Authorize(normalizedProjectRoot);
        if (!authorization.Allowed)
        {
            return ImportFailure(
                authorization.Code,
                "$.projectRoot",
                authorization.Message,
                authorization.RepairSuggestion,
                requiresConfirmation: false);
        }

        if (ProjectWritePolicy.FindReparsePoint(normalizedProjectRoot, destinationRoot) is { } reparsePoint)
        {
            return ImportFailure(
                "PackImportPathUsesReparsePoint",
                "$.projectRoot",
                $"Pack import path uses a reparse point: {reparsePoint}.",
                "Choose a projectRoot whose .wpfdevtools parent path uses ordinary local directories.",
                requiresConfirmation: false);
        }

        try
        {
            var imported = await PackImportService.ImportAsync(
                    normalizedArchivePath,
                    destinationRoot,
                    "project-local",
                    reviewedArchiveSha256,
                    allowOverwrite,
                    limits: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return ToImportPayload(imported, destinationRoot, imported: true, requiresConfirmation: false);
        }
        catch (PackImportPlanChangedException)
        {
            return ImportPlanChangedFailure();
        }
    }

    private static string NormalizeImportProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("projectRoot is required.", nameof(projectRoot));
        }

        var fullPath = Path.GetFullPath(projectRoot);
        if (!ProjectWritePolicy.IsLocalAbsolutePath(fullPath)
            || ProjectWritePolicy.IsSystemDirectoryPath(fullPath))
        {
            throw new ArgumentException("projectRoot must be a reviewed local absolute non-system path.", nameof(projectRoot));
        }

        return fullPath;
    }

    private static string NormalizeArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("archivePath is required.", nameof(archivePath));
        }

        var fullPath = Path.GetFullPath(archivePath);
        if (!ProjectWritePolicy.IsLocalAbsolutePath(fullPath))
        {
            throw new ArgumentException("archivePath must be a local absolute path.", nameof(archivePath));
        }

        return fullPath;
    }

    private static object ToImportPayload(
        PackImportPlan plan,
        string destinationRoot,
        bool imported,
        bool requiresConfirmation)
        => new
        {
            success = true,
            plan.DryRun,
            imported,
            requiresConfirmation,
            plan.PackId,
            plan.Version,
            plan.ArchiveSha256,
            destinationRoot,
            filePlan = plan.FilePlan.Select(file => new { file.RelativePath, sizeBytes = file.Length }).ToArray(),
            plan.WouldModifyProjectFiles,
            plan.WouldRunNuGetRestore,
            plan.Observability
        };

    private static object ImportFailure(
        string code,
        string jsonPath,
        string message,
        string repairSuggestion,
        bool requiresConfirmation)
        => new
        {
            success = false,
            dryRun = false,
            requiresConfirmation,
            errors = new[] { new { jsonPath, code, message, repairSuggestion } }
        };

    private static object ImportPlanChangedFailure()
        => ImportFailure(
            "ImportPlanChanged",
            "$.reviewedArchiveSha256",
            "Pack archive content changed after review.",
            "Run a new dry import, review the new file plan, and confirm its archiveSha256.",
            requiresConfirmation: true);
}
