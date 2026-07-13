using System.Text;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed class UiBlueprintProjectIntegrationService(PackRegistry registry)
{
    private static readonly HashSet<string> AllowedRoles =
    [
        "package-reference",
        "central-package-version",
        "application-xaml",
        "code-behind-base-type"
    ];

    public ProjectIntegrationResult Apply(ProjectIntegrationRequest request)
    {
        if (!request.ConfirmIntegration)
        {
            return Invalid(
                request.ReviewedPlanHash,
                "$.confirmIntegration",
                "IntegrationConfirmationRequired",
                "Project integration requires explicit confirmIntegration=true.",
                "Review projectIntegrationPlan and its planHash, then confirm that exact plan.");
        }

        if (string.IsNullOrWhiteSpace(request.ReviewedPlanHash))
        {
            return Invalid(
                string.Empty,
                "$.reviewedPlanHash",
                "ReviewedPlanHashRequired",
                "reviewedPlanHash is required for project integration.",
                "Pass the planHash returned by the latest apply_ui_blueprint dry-run.");
        }

        var projectRoot = NormalizeProjectRoot(request.ProjectRoot);
        if (projectRoot.Error is not null)
        {
            return ProjectIntegrationResult.Invalid(request.ReviewedPlanHash, [projectRoot.Error]);
        }

        var authorization = ProjectWritePolicy.Authorize(projectRoot.Path!);
        if (!authorization.Allowed)
        {
            return ProjectIntegrationResult.Invalid(
                request.ReviewedPlanHash,
                [new ApplyBlueprintIssue(
                    "$.projectRoot",
                    authorization.Code,
                    authorization.Message,
                    authorization.RepairSuggestion)]);
        }

        var dryRun = new UiBlueprintApplyService(registry).Apply(new ApplyBlueprintRequest(
            request.BlueprintJson,
            projectRoot.Path!,
            request.TargetPath));
        if (!dryRun.Success || !dryRun.ProjectIntegrationPlan.Ready)
        {
            var errors = dryRun.Errors.Concat(dryRun.ProjectIntegrationPlan.Errors).ToArray();
            return ProjectIntegrationResult.Invalid(request.ReviewedPlanHash, errors);
        }

        var plan = dryRun.ProjectIntegrationPlan;
        if (!string.Equals(plan.PlanHash, request.ReviewedPlanHash, StringComparison.Ordinal))
        {
            return Invalid(
                plan.PlanHash,
                "$.reviewedPlanHash",
                "IntegrationPlanChanged",
                "The current project integration plan differs from the reviewed plan.",
                "Review the newly returned planHash and operations before confirming again.");
        }

        var validation = ValidateOperations(projectRoot.Path!, plan.Operations);
        if (validation.Count > 0)
        {
            return ProjectIntegrationResult.Invalid(plan.PlanHash, validation);
        }

        return ApplyOperations(projectRoot.Path!, plan);
    }

    private static ProjectIntegrationResult ApplyOperations(
        string projectRoot,
        ProjectIntegrationPlan plan)
    {
        var changes = new List<ProjectIntegrationChange>();
        var backupRoot = Path.Combine(
            projectRoot,
            ".wpfdevtools-backups",
            "project-integration",
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        if (ProjectWritePolicy.FindReparsePoint(projectRoot, backupRoot) is { } backupReparsePoint)
        {
            return Invalid(
                plan.PlanHash,
                "$.projectRoot",
                "ProjectBackupPathUsesReparsePoint",
                $"Project integration backup path uses a reparse point: {backupReparsePoint}.",
                "Remove the backup directory reparse point or choose a project root without redirected backup paths.");
        }

        foreach (var operation in plan.Operations.Where(operation => operation.Action != "none"))
        {
            string? tempPath = null;
            try
            {
                var precondition = ReadPrecondition(operation.TargetPath);
                if (precondition != operation.Precondition)
                {
                    return RollBack(
                        plan.PlanHash,
                        changes,
                        new ApplyBlueprintIssue(
                            "$.reviewedPlanHash",
                            "IntegrationPlanChanged",
                            $"Integration target changed after review: {operation.TargetPath}.",
                            "Review a fresh dry-run plan before confirming again."));
                }

                var targetDirectory = Path.GetDirectoryName(operation.TargetPath)!;
                Directory.CreateDirectory(targetDirectory);
                tempPath = Path.Combine(
                    targetDirectory,
                    "." + Path.GetFileName(operation.TargetPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(tempPath, operation.ProposedContent, Encoding.UTF8);

                string? backupPath = null;
                if (operation.Precondition.Exists)
                {
                    backupPath = Path.Combine(backupRoot, Path.GetRelativePath(projectRoot, operation.TargetPath));
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Replace(tempPath, operation.TargetPath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, operation.TargetPath);
                }

                changes.Add(new ProjectIntegrationChange(
                    operation.Role,
                    operation.TargetPath,
                    operation.Action,
                    backupPath,
                    operation.Precondition.Exists ? "restore-backup" : "delete-created-file"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(tempPath);
                return RollBack(
                    plan.PlanHash,
                    changes,
                    new ApplyBlueprintIssue(
                        "$.projectIntegrationPlan",
                        "IntegrationWriteFailed",
                        $"Project integration failed while applying '{operation.Role}' to '{operation.TargetPath}': {ex.Message}",
                        "Resolve the file lock or permission issue, rerun dry-run, and review the new plan."));
            }
        }

        return new ProjectIntegrationResult(true, true, false, plan.PlanHash, changes, []);
    }

    private static ProjectIntegrationResult RollBack(
        string planHash,
        List<ProjectIntegrationChange> changes,
        ApplyBlueprintIssue cause)
    {
        var errors = new List<ApplyBlueprintIssue> { cause };
        foreach (var change in changes.AsEnumerable().Reverse())
        {
            try
            {
                if (change.BackupPath is not null)
                {
                    File.Copy(change.BackupPath, change.TargetPath, overwrite: true);
                }
                else
                {
                    File.Delete(change.TargetPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(new ApplyBlueprintIssue(
                    "$.projectIntegrationPlan",
                    "IntegrationRollbackFailed",
                    $"Rollback failed for '{change.TargetPath}': {ex.Message}",
                    "Restore the recorded backup manually before retrying integration."));
            }
        }

        var rolledBack = changes.Count > 0 && errors.All(error => error.Code != "IntegrationRollbackFailed");
        return ProjectIntegrationResult.Invalid(planHash, errors, rolledBack, changes);
    }

    private static List<ApplyBlueprintIssue> ValidateOperations(
        string projectRoot,
        IReadOnlyList<ProjectIntegrationOperation> operations)
    {
        var errors = new List<ApplyBlueprintIssue>();
        foreach (var operation in operations)
        {
            if (!AllowedRoles.Contains(operation.Role)
                || !ProjectWritePolicy.IsPathUnderRoot(projectRoot, operation.TargetPath))
            {
                errors.Add(new ApplyBlueprintIssue(
                    "$.projectIntegrationPlan.operations",
                    "IntegrationOperationBlocked",
                    $"Integration operation '{operation.Role}' is outside the guarded project contract.",
                    "Use only the operations returned by the current Composer dry-run plan."));
                continue;
            }

            if (ProjectWritePolicy.IsProtectedMetadataPath(projectRoot, operation.TargetPath))
            {
                errors.Add(new ApplyBlueprintIssue(
                    "$.projectIntegrationPlan.operations",
                    "ProtectedProjectPath",
                    $"Integration target is inside protected metadata: {operation.TargetPath}.",
                    "Choose an ordinary WPF project root and regenerate the plan."));
            }

            if (ProjectWritePolicy.FindReparsePoint(projectRoot, operation.TargetPath) is { } reparsePoint)
            {
                errors.Add(new ApplyBlueprintIssue(
                    "$.projectIntegrationPlan.operations",
                    "ProjectPathUsesReparsePoint",
                    $"Integration target uses a reparse point: {reparsePoint}.",
                    "Remove the reparse point or choose a project root without redirected integration paths."));
            }
        }

        return errors;
    }

    private static ProjectFilePrecondition ReadPrecondition(string path)
    {
        if (!File.Exists(path))
        {
            return new ProjectFilePrecondition(false, string.Empty);
        }

        return new ProjectFilePrecondition(
            true,
            ProjectIntegrationPlanBuilder.Sha256(File.ReadAllText(path)));
    }

    private static (string? Path, ApplyBlueprintIssue? Error) NormalizeProjectRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, new ApplyBlueprintIssue(
                "$.projectRoot",
                "ProjectRootRequired",
                "projectRoot is required.",
                "Pass the reviewed local WPF project root."));
        }

        try
        {
            var path = Path.GetFullPath(value);
            if (!ProjectWritePolicy.IsLocalAbsolutePath(path) || ProjectWritePolicy.IsSystemDirectoryPath(path))
            {
                return (null, new ApplyBlueprintIssue(
                    "$.projectRoot",
                    "InvalidProjectRoot",
                    "projectRoot must be a non-system local absolute path.",
                    "Choose the exact reviewed WPF project root."));
            }

            return (path, null);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, new ApplyBlueprintIssue(
                "$.projectRoot",
                "InvalidProjectRoot",
                $"projectRoot is invalid: {ex.Message}",
                "Pass a valid local absolute project root."));
        }
    }

    private static ProjectIntegrationResult Invalid(
        string planHash,
        string jsonPath,
        string code,
        string message,
        string repair)
        => ProjectIntegrationResult.Invalid(
            planHash,
            [new ApplyBlueprintIssue(jsonPath, code, message, repair)]);

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
