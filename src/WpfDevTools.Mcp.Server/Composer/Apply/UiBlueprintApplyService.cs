using System.Text;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed class UiBlueprintApplyService(PackRegistry registry)
{
    private const string BlueprintHeaderPrefix = "<!-- WPFDEVTOOLS_BLUEPRINT_SOURCE: ";
    private const string SafeSlotBegin = "<!-- WPFDEVTOOLS_SAFE_SLOT_BEGIN: manual-content -->";
    private const string SafeSlotEnd = "<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->";

    public ApplyBlueprintResult Apply(ApplyBlueprintRequest request)
    {
        var errors = new List<ApplyBlueprintIssue>();
        var projectRoot = NormalizeProjectRoot(request.ProjectRoot, errors);
        var targetPath = ResolveTargetPath(projectRoot, request, errors);
        if (errors.Count > 0)
        {
            return ApplyBlueprintResult.Invalid(request.DryRun, errors);
        }

        var render = new UiBlueprintRenderer(registry)
            .Render(new RenderBlueprintRequest(request.BlueprintJson, targetPath));
        if (!render.Valid)
        {
            return ApplyBlueprintResult.Invalid(
                request.DryRun,
                render.Errors.Select(ApplyBlueprintIssue.FromValidationIssue).ToArray());
        }

        var generatedXaml = AddComposerHeaderAndSafeSlot(
            request.BlueprintJson,
            render.Xaml,
            File.Exists(targetPath) ? File.ReadAllText(targetPath) : null);
        var viewModelContract = CreateViewModelContract(projectRoot, targetPath, render.RequiredNuGetPackages);
        var filePlan = CreateFilePlan(targetPath, viewModelContract.TargetPath, request.DryRun, backupPath: null);

        if (request.DryRun)
        {
            return ApplyBlueprintResult.CreateValid(
                dryRun: true,
                wouldWriteFiles: false,
                generatedXaml,
                filePlan,
                render.RequiredResources,
                render.RequiredNuGetPackages,
                viewModelContract with { WouldWrite = false },
                []);
        }

        var authorization = ProjectWritePolicy.Authorize(projectRoot);
        if (!authorization.Allowed)
        {
            return ApplyBlueprintResult.Invalid(
                dryRun: false,
                [new ApplyBlueprintIssue("$.projectRoot", authorization.Code, authorization.Message, authorization.RepairSuggestion)]);
        }

        if (ProjectWritePolicy.FindReparsePoint(projectRoot, targetPath) is { } reparsePoint)
        {
            return ApplyBlueprintResult.Invalid(
                dryRun: false,
                [new ApplyBlueprintIssue(
                    "$.targetPath",
                    "ProjectPathUsesReparsePoint",
                    $"Project write path uses a reparse point: {reparsePoint}.",
                    "Choose a targetPath whose existing parent directories are ordinary directories inside the reviewed projectRoot.")]);
        }

        var backupPath = WriteViewFile(projectRoot, targetPath, generatedXaml);
        return ApplyBlueprintResult.CreateValid(
            dryRun: false,
            wouldWriteFiles: true,
            generatedXaml,
                CreateFilePlan(targetPath, viewModelContract.TargetPath, dryRun: false, backupPath),
            render.RequiredResources,
            render.RequiredNuGetPackages,
            viewModelContract with { WouldWrite = false },
            []);
    }

    private static string NormalizeProjectRoot(string projectRoot, List<ApplyBlueprintIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            errors.Add(new ApplyBlueprintIssue("$.projectRoot", "ProjectRootRequired", "projectRoot is required.", "Pass the reviewed local WPF project root."));
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(projectRoot);
        if (!ProjectWritePolicy.IsLocalAbsolutePath(fullPath))
        {
            errors.Add(new ApplyBlueprintIssue("$.projectRoot", "InvalidProjectRoot", "projectRoot must be a local absolute path.", "Use a local absolute project root path."));
        }

        return fullPath;
    }

    private static string ResolveTargetPath(
        string projectRoot,
        ApplyBlueprintRequest request,
        List<ApplyBlueprintIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return string.Empty;
        }

        var targetPath = string.IsNullOrWhiteSpace(request.TargetPath)
            ? Path.Combine(projectRoot, "Views", SanitizeFileName(ReadBlueprintName(request.BlueprintJson)) + ".xaml")
            : Path.IsPathFullyQualified(request.TargetPath)
                ? request.TargetPath
                : Path.Combine(projectRoot, request.TargetPath);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!ProjectWritePolicy.IsPathUnderRoot(projectRoot, fullTargetPath))
        {
            errors.Add(new ApplyBlueprintIssue("$.targetPath", "ProjectPathOutsideRoot", "targetPath must stay inside projectRoot.", "Choose a targetPath under the reviewed projectRoot."));
        }

        return fullTargetPath;
    }

    private static string ReadBlueprintName(string blueprintJson)
    {
        try
        {
            using var document = JsonDocument.Parse(blueprintJson);
            return document.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() ?? "GeneratedView"
                : "GeneratedView";
        }
        catch (JsonException)
        {
            return "GeneratedView";
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "GeneratedView" : sanitized;
    }

    private static string AddComposerHeaderAndSafeSlot(string blueprintJson, string xaml, string? existingContent)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(blueprintJson));
        var preservedSlot = ExtractSafeSlot(existingContent) ?? $"{SafeSlotBegin}{Environment.NewLine}{SafeSlotEnd}";
        return string.Join(
            Environment.NewLine,
            $"{BlueprintHeaderPrefix}{encoded} -->",
            xaml,
            preservedSlot,
            string.Empty);
    }

    private static string? ExtractSafeSlot(string? existingContent)
    {
        if (string.IsNullOrEmpty(existingContent))
        {
            return null;
        }

        var begin = existingContent.IndexOf(SafeSlotBegin, StringComparison.Ordinal);
        var end = existingContent.IndexOf(SafeSlotEnd, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
        {
            return null;
        }

        return existingContent[begin..(end + SafeSlotEnd.Length)];
    }

    private static ApplyFilePlanItem[] CreateFilePlan(
        string targetPath,
        string viewModelContractPath,
        bool dryRun,
        string? backupPath)
        =>
        [
            new(
                Role: "view",
                TargetPath: targetPath,
                Action: File.Exists(targetPath) ? "update" : "create",
                WouldWrite: !dryRun,
                BackupPath: backupPath,
                Reversible: backupPath is not null || dryRun),
            new(
                Role: "viewmodel-binding-contract",
                TargetPath: viewModelContractPath,
                Action: "plan",
                WouldWrite: false,
                BackupPath: null,
                Reversible: true)
        ];

    private static ViewModelBindingContractPlan CreateViewModelContract(
        string projectRoot,
        string targetPath,
        IReadOnlyList<RequiredNuGetPackage> packages)
        => new(
            TargetPath: Path.Combine(projectRoot, "ViewModels", Path.GetFileNameWithoutExtension(targetPath) + ".Bindings.json"),
            Content: JsonSerializer.Serialize(new
            {
                schemaVersion = "wpfdevtools.viewmodel-binding-contract.v1",
                view = Path.GetFileName(targetPath),
                requiredPackages = packages.Select(package => package.Id).ToArray()
            }),
            WouldWrite: false);

    private static string? WriteViewFile(string projectRoot, string targetPath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string? backupPath = null;
        if (File.Exists(targetPath))
        {
            var relative = Path.GetRelativePath(projectRoot, targetPath);
            backupPath = Path.Combine(projectRoot, ".wpfdevtools-backups", DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(targetPath, backupPath, overwrite: false);
        }

        File.WriteAllText(targetPath, content, Encoding.UTF8);
        return backupPath;
    }
}

internal static class ProjectWritePolicy
{
    public static ProjectWriteAuthorization Authorize(string projectRoot)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(McpServerConfiguration.AllowProjectWritesEnvVar),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return ProjectWriteAuthorization.Denied(
                "ProjectWritesDisabled",
                $"Project writes are disabled by default. Set {McpServerConfiguration.AllowProjectWritesEnvVar}=true.",
                "Enable project writes only after reviewing the generated file plan.");
        }

        var configuredRoots = Environment.GetEnvironmentVariable(McpServerConfiguration.AllowedProjectRootsEnvVar);
        var roots = ParseAllowedRoots(configuredRoots);
        if (!roots.Valid)
        {
            return ProjectWriteAuthorization.Denied(
                "InvalidProjectRootAllowlist",
                $"{McpServerConfiguration.AllowedProjectRootsEnvVar} contains a non-local or non-absolute root.",
                "Use semicolon-separated local absolute project roots.");
        }

        if (roots.Count == 0)
        {
            return ProjectWriteAuthorization.Denied(
                "ProjectRootNotAllowlisted",
                $"No project root is allowlisted in {McpServerConfiguration.AllowedProjectRootsEnvVar}.",
                "Set the allowed project roots environment variable to the reviewed local project root.");
        }

        var normalizedProjectRoot = NormalizeRoot(projectRoot);
        return roots.Roots.Any(root => string.Equals(root, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
            ? ProjectWriteAuthorization.CreateAllowed()
            : ProjectWriteAuthorization.Denied(
                "ProjectRootNotAllowlisted",
                "projectRoot is not allowlisted for UI Composer writes.",
                $"Add the exact projectRoot to {McpServerConfiguration.AllowedProjectRootsEnvVar}.");
    }

    public static bool IsLocalAbsolutePath(string path)
        => Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal);

    public static bool IsPathUnderRoot(string root, string candidate)
    {
        var normalizedRoot = NormalizeRoot(root);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public static string? FindReparsePoint(string root, string candidate)
    {
        var normalizedRoot = NormalizeRoot(root);
        var current = normalizedRoot;
        if (HasReparsePoint(current))
        {
            return current;
        }

        var targetParent = Path.GetDirectoryName(Path.GetFullPath(candidate));
        if (string.IsNullOrWhiteSpace(targetParent))
        {
            return null;
        }

        var relativeParent = Path.GetRelativePath(normalizedRoot, targetParent);
        foreach (var part in relativeParent.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            current = Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                break;
            }

            if (HasReparsePoint(current))
            {
                return current;
            }
        }

        return File.Exists(candidate) && HasReparsePoint(candidate)
            ? candidate
            : null;
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static AllowedProjectRoots ParseAllowedRoots(string? configuredRoots)
    {
        if (string.IsNullOrWhiteSpace(configuredRoots))
        {
            return new AllowedProjectRoots(true, []);
        }

        var roots = new List<string>();
        foreach (var entry in configuredRoots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsLocalAbsolutePath(entry))
            {
                return new AllowedProjectRoots(false, []);
            }

            roots.Add(NormalizeRoot(entry));
        }

        return new AllowedProjectRoots(true, roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string NormalizeRoot(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

internal sealed record AllowedProjectRoots(bool Valid, IReadOnlyList<string> Roots)
{
    public int Count => Roots.Count;
}

internal sealed record ApplyBlueprintRequest(
    string BlueprintJson,
    string ProjectRoot,
    string? TargetPath = null,
    bool DryRun = true);

internal sealed record ApplyBlueprintResult(
    bool Success,
    bool Valid,
    bool DryRun,
    bool WouldWriteFiles,
    string Xaml,
    IReadOnlyList<ApplyFilePlanItem> FilePlan,
    IReadOnlyList<string> ResourcePlan,
    IReadOnlyList<RequiredNuGetPackage> RequiredNuGetPackages,
    ViewModelBindingContractPlan ViewModelBindingContract,
    IReadOnlyList<ApplyBlueprintIssue> Errors)
{
    public static ApplyBlueprintResult CreateValid(
        bool dryRun,
        bool wouldWriteFiles,
        string xaml,
        IReadOnlyList<ApplyFilePlanItem> filePlan,
        IReadOnlyList<string> resourcePlan,
        IReadOnlyList<RequiredNuGetPackage> packages,
        ViewModelBindingContractPlan viewModelBindingContract,
        IReadOnlyList<ApplyBlueprintIssue> errors)
        => new(true, true, dryRun, wouldWriteFiles, xaml, filePlan, resourcePlan, packages, viewModelBindingContract, errors);

    public static ApplyBlueprintResult Invalid(bool dryRun, IReadOnlyList<ApplyBlueprintIssue> errors)
        => new(false, false, dryRun, false, string.Empty, [], [], [], new ViewModelBindingContractPlan(string.Empty, string.Empty, false), errors);
}

internal sealed record ApplyFilePlanItem(
    string Role,
    string TargetPath,
    string Action,
    bool WouldWrite,
    string? BackupPath,
    bool Reversible);

internal sealed record ViewModelBindingContractPlan(string TargetPath, string Content, bool WouldWrite);

internal sealed record ApplyBlueprintIssue(string JsonPath, string Code, string Message, string RepairSuggestion)
{
    public static ApplyBlueprintIssue FromValidationIssue(Composer.Blueprints.BlueprintValidationIssue issue)
        => new(issue.JsonPath, issue.Code, issue.Message, issue.RepairSuggestion);
}

internal sealed record ProjectWriteAuthorization(
    bool Allowed,
    string Code,
    string Message,
    string RepairSuggestion)
{
    public static ProjectWriteAuthorization CreateAllowed()
        => new(true, string.Empty, string.Empty, string.Empty);

    public static ProjectWriteAuthorization Denied(string code, string message, string repairSuggestion)
        => new(false, code, message, repairSuggestion);
}
