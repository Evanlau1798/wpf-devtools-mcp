using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed partial class UiBlueprintApplyService(PackRegistry registry)
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

        var behaviorContract = BehaviorIntegrationContractBuilder.Build(registry, request.BlueprintJson);
        var bindingRequirements = ViewModelBindingRequirementBuilder.Build(registry, request.BlueprintJson);
        var viewModelContract = CreateViewModelContract(
            projectRoot,
            targetPath,
            render.RequiredNuGetPackages,
            behaviorContract,
            bindingRequirements);
        var codeBehind = CodeBehindIntegrationResolver.Resolve(registry, request.BlueprintJson, targetPath);
        var appliedXaml = AddProjectMainWindowClass(projectRoot, targetPath, render.Xaml, codeBehind);
        var projectIntegrationPlan = ProjectIntegrationPlanBuilder.Build(
            registry,
            request.BlueprintJson,
            projectRoot,
            targetPath,
            appliedXaml,
            render.RequiredNuGetPackages,
            render.RequiredResources,
            codeBehind);

        if (request.DryRun)
        {
            var dryRunXaml = AddComposerHeaderAndSafeSlot(request.BlueprintJson, appliedXaml, existingContent: null);
            var filePlan = CreateFilePlan(
                targetPath,
                viewModelContract.TargetPath,
                request.DryRun,
                targetExisted: File.Exists(targetPath),
                backupPath: null,
                codeBehind);
            return ApplyBlueprintResult.CreateValid(
                dryRun: true,
                requiresConfirmation: true,
                wouldWriteFiles: false,
                dryRunXaml,
                filePlan,
                render.RequiredResources,
                render.RequiredNuGetPackages,
                viewModelContract with { WouldWrite = false },
                behaviorContract,
                projectIntegrationPlan,
                []);
        }

        if (!request.ConfirmApply)
        {
            return ApplyBlueprintResult.Invalid(
                dryRun: false,
                [new ApplyBlueprintIssue(
                    "$.confirmApply",
                    "ApplyConfirmationRequired",
                    "Non-dry-run UI Composer apply requires explicit confirmApply=true.",
                    "Review the dry-run file plan, then retry with confirmApply=true only for the reviewed project root and target path.")],
                requiresConfirmation: true);
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

        var existingContent = ReadExistingContent(targetPath);
        if (!existingContent.Success)
        {
            return ApplyBlueprintResult.Invalid(dryRun: false, [existingContent.Error!]);
        }

        var generatedXaml = AddComposerHeaderAndSafeSlot(request.BlueprintJson, appliedXaml, existingContent.Content);
        var write = WriteViewFile(projectRoot, targetPath, generatedXaml);
        if (!write.Success)
        {
            return ApplyBlueprintResult.Invalid(dryRun: false, [write.Error!]);
        }

        return ApplyBlueprintResult.CreateValid(
            dryRun: false,
            requiresConfirmation: false,
            wouldWriteFiles: true,
            generatedXaml,
            CreateFilePlan(targetPath, viewModelContract.TargetPath, false, write.TargetExisted, write.BackupPath, codeBehind),
            render.RequiredResources,
            render.RequiredNuGetPackages,
            viewModelContract with { WouldWrite = false },
            behaviorContract,
            projectIntegrationPlan,
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

        if (ProjectWritePolicy.IsSystemDirectoryPath(fullPath))
        {
            errors.Add(new ApplyBlueprintIssue(
                "$.projectRoot",
                "ProjectRootIsSystemDirectory",
                "projectRoot must not be a system directory.",
                "Choose a reviewed WPF project root outside Windows, Program Files, and system data directories."));
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

        if (!string.IsNullOrWhiteSpace(request.TargetPath)
            && Path.IsPathFullyQualified(request.TargetPath))
        {
            errors.Add(new ApplyBlueprintIssue(
                "$.targetPath",
                "AbsoluteTargetPathBlocked",
                "targetPath must be project-root relative.",
                "Pass a relative targetPath such as Views/GeneratedView.xaml."));
        }

        var targetPath = string.IsNullOrWhiteSpace(request.TargetPath)
            ? Path.Combine(projectRoot, "Views", SanitizeFileName(ReadBlueprintName(request.BlueprintJson)) + ".xaml")
            : Path.Combine(projectRoot, request.TargetPath);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!ProjectWritePolicy.IsPathUnderRoot(projectRoot, fullTargetPath))
        {
            errors.Add(new ApplyBlueprintIssue("$.targetPath", "ProjectPathOutsideRoot", "targetPath must stay inside projectRoot.", "Choose a targetPath under the reviewed projectRoot."));
        }

        if (ProjectWritePolicy.IsProtectedMetadataPath(projectRoot, fullTargetPath))
        {
            errors.Add(new ApplyBlueprintIssue(
                "$.targetPath",
                "ProtectedProjectPath",
                "targetPath must not point inside protected project metadata directories.",
                "Choose a targetPath outside .git and other metadata directories."));
        }

        if (ProjectWritePolicy.IsBlockedProjectFileTarget(projectRoot, fullTargetPath))
        {
            errors.Add(new ApplyBlueprintIssue(
                "$.targetPath",
                "ProjectFilePolicyViolation",
                "UI Composer apply does not write project, App.xaml, ResourceDictionary, or ViewModel files by default.",
                "Use apply_ui_blueprint for generated view XAML only and create a separate reviewed plan for project, resource, package, or ViewModel changes."));
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

    private static string AddProjectMainWindowClass(
        string projectRoot,
        string targetPath,
        string xaml,
        CodeBehindIntegrationPlan? codeBehind)
    {
        var hasExistingCodeBehind = File.Exists(Path.ChangeExtension(targetPath, ".xaml.cs"));
        if ((!hasExistingCodeBehind && codeBehind is null)
            || xaml.Contains("x:Class=", StringComparison.Ordinal))
        {
            return xaml;
        }

        var tagEnd = xaml.IndexOf('>', StringComparison.Ordinal);
        if (tagEnd < 0)
        {
            return xaml;
        }

        var insertAt = tagEnd > 0 && xaml[tagEnd - 1] == '/' ? tagEnd - 1 : tagEnd;
        var xmlns = xaml.Contains("xmlns:x=", StringComparison.Ordinal)
            ? string.Empty
            : " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        return xaml.Insert(insertAt, $"{xmlns} x:Class=\"{ResolveProjectMainWindowClass(projectRoot, targetPath)}\"");
    }

    private static string ResolveProjectMainWindowClass(string projectRoot, string targetPath)
        => $"{ResolveRootNamespace(projectRoot)}.{ComposerCSharpIdentifier.Create(Path.GetFileNameWithoutExtension(targetPath), "MainWindow")}";

    private static string ResolveRootNamespace(string projectRoot)
    {
        if (!Directory.Exists(projectRoot))
        {
            return ComposerCSharpIdentifier.Create(Path.GetFileName(projectRoot), "Application");
        }

        var projectFile = Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (projectFile is null)
        {
            return ComposerCSharpIdentifier.Create(Path.GetFileName(projectRoot), "Application");
        }

        try
        {
            var rootNamespace = XDocument.Load(projectFile)
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "RootNamespace")
                ?.Value;
            return string.IsNullOrWhiteSpace(rootNamespace)
                ? ComposerCSharpIdentifier.Create(Path.GetFileNameWithoutExtension(projectFile), "Application")
                : SanitizeNamespace(rootNamespace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return ComposerCSharpIdentifier.Create(Path.GetFileNameWithoutExtension(projectFile), "Application");
        }
    }

    private static string SanitizeNamespace(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => ComposerCSharpIdentifier.Create(part, "Application"))
            .ToArray();
        return parts.Length == 0 ? "Application" : string.Join(".", parts);
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
        bool targetExisted,
        string? backupPath,
        CodeBehindIntegrationPlan? codeBehind)
    {
        var items = new List<ApplyFilePlanItem>
        {
            new(
                Role: "view",
                TargetPath: targetPath,
                Action: targetExisted ? "update" : "create",
                WouldWrite: !dryRun,
                RiskLevel: targetExisted ? "medium" : "low",
                BackupPath: backupPath,
                Reversible: !targetExisted || backupPath is not null || dryRun),
            new(
                Role: "viewmodel-binding-contract",
                TargetPath: viewModelContractPath,
                Action: "plan",
                WouldWrite: false,
                RiskLevel: "low",
                BackupPath: null,
                Reversible: true)
        };

        if (codeBehind is not null)
        {
            items.Add(new(
                Role: "code-behind-integration",
                TargetPath: codeBehind.TargetPath,
                Action: $"plan update base class to {codeBehind.BaseType} for the generated x:Class",
                WouldWrite: false,
                RiskLevel: "medium",
                BackupPath: null,
                Reversible: true));
        }

        return [.. items];
    }

    private static ViewModelBindingContractPlan CreateViewModelContract(
        string projectRoot,
        string targetPath,
        IReadOnlyList<RequiredNuGetPackage> packages,
        BehaviorIntegrationContractPlan behaviorContract,
        IReadOnlyList<ViewModelBindingRequirement> bindingRequirements)
    {
        var requirementsContract = new
        {
            status = bindingRequirements.Count == 0 ? "not-detected" : "required",
            implementationReadiness = bindingRequirements.Count == 0
                ? "not-required"
                : "project-implementation-required",
            composerWritesViewModelSource = false,
            requirements = bindingRequirements.Select(requirement => new
            {
                bindingStatus = requirement.BindingStatus,
                bindingPath = requirement.BindingPath,
                rawBindings = requirement.RawBindings,
                implementationStatus = "required",
                usages = requirement.Usages.Select(usage => new
                {
                    jsonPath = usage.JsonPath,
                    blockKind = usage.BlockKind,
                    propertyName = usage.PropertyName,
                    declaredPropertyType = usage.DeclaredPropertyType,
                    rawBinding = usage.RawBinding
                })
            }),
            implementationGuidance = bindingRequirements.Count == 0
                ? "No authored ViewModel bindings were detected."
                : "Implement every resolved binding path in the project ViewModel and resolve every path-unresolved binding. Composer does not write ViewModel source.",
            verificationGuidance = "Build and launch the final app, then verify every listed usage has an active binding without runtime binding errors."
        };
        var content = JsonSerializer.Serialize(new
        {
            schemaVersion = "wpfdevtools.viewmodel-binding-contract.v1",
            view = Path.GetFileName(targetPath),
            requiredPackages = packages.Select(package => package.Id).ToArray(),
            bindingRequirements = requirementsContract,
            behaviorIntegration = new
            {
                status = behaviorContract.Status,
                sourceRecipeId = behaviorContract.SourceRecipeId,
                interactions = behaviorContract.Interactions.Select(interaction => new
                {
                    kind = interaction.Kind,
                    commandPath = interaction.CommandPath,
                    commandParameter = interaction.CommandParameter,
                    targetPageTag = interaction.TargetPageTag,
                    label = interaction.Label,
                    implementationGuidance = interaction.ImplementationGuidance
                }),
                implementationGuidance = behaviorContract.ImplementationGuidance,
                verificationGuidance = behaviorContract.VerificationGuidance
            }
        });

        return new(
            TargetPath: Path.Combine(projectRoot, "ViewModels", Path.GetFileNameWithoutExtension(targetPath) + ".Bindings.json"),
            Content: content,
            WouldWrite: false,
            BindingRequirements: JsonSerializer.SerializeToElement(requirementsContract));
    }

}
