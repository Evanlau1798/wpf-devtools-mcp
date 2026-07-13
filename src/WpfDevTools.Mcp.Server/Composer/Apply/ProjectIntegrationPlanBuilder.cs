using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ProjectIntegrationPlanBuilder
{
    public static ProjectIntegrationPlan Build(
        PackRegistry registry,
        string blueprintJson,
        string projectRoot,
        string targetPath,
        string appliedXaml,
        IReadOnlyList<RequiredNuGetPackage> packages,
        IReadOnlyList<string> resources,
        CodeBehindIntegrationPlan? codeBehind)
    {
        var errors = new List<ApplyBlueprintIssue>();
        var operations = new List<ProjectIntegrationOperation>();
        AddPackageOperations(projectRoot, packages, operations, errors);
        AddApplicationOperation(
            registry,
            blueprintJson,
            projectRoot,
            targetPath,
            appliedXaml,
            resources,
            codeBehind is not null,
            operations,
            errors);
        AddCodeBehindOperation(appliedXaml, codeBehind, operations, errors);
        if (errors.Count > 0)
        {
            return new ProjectIntegrationPlan(false, string.Empty, operations, errors);
        }

        return new ProjectIntegrationPlan(true, ComputePlanHash(operations), operations, []);
    }

    private static void AddPackageOperations(
        string projectRoot,
        IReadOnlyList<RequiredNuGetPackage> packages,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        if (packages.Count == 0)
        {
            return;
        }

        var guidance = PackageIntegrationPlanner.Create(projectRoot, packages);
        if (guidance.Mode is not "project" and not "central" || string.IsNullOrWhiteSpace(guidance.ProjectFile))
        {
            errors.Add(Issue(
                "IntegrationProjectFileMissing",
                "A machine-applicable package patch requires one inspectable project file.",
                "Add or select a WPF project file under projectRoot, then rerun the dry-run plan."));
            return;
        }

        var projectPath = ResolveInsideRoot(projectRoot, guidance.ProjectFile, "project file", errors);
        if (projectPath is not null)
        {
            AddPatchedOperation(
                "package-reference",
                projectPath,
                ["packages"],
                ProjectIntegrationXmlPatcher.PatchProjectPackages(projectPath, packages, guidance.Mode == "central"),
                "Add pack-declared PackageReference items using the inspected package-management mode.",
                operations,
                errors);
        }

        if (guidance.Mode != "central")
        {
            return;
        }

        var centralPath = ResolveInsideRoot(projectRoot, guidance.CentralPackageFile, "central package file", errors);
        if (centralPath is not null)
        {
            AddPatchedOperation(
                "central-package-version",
                centralPath,
                ["packages"],
                ProjectIntegrationXmlPatcher.PatchCentralPackages(centralPath, packages),
                "Add pack-declared PackageVersion items to the project-local central package file.",
                operations,
                errors);
        }
    }

    private static void AddApplicationOperation(
        PackRegistry registry,
        string blueprintJson,
        string projectRoot,
        string targetPath,
        string appliedXaml,
        IReadOnlyList<string> resources,
        bool setStartup,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        var selectedManifests = ResolveSelectedManifests(registry, blueprintJson);
        setStartup = setStartup || IsWindowRoot(selectedManifests, appliedXaml);
        if (resources.Count == 0 && !setStartup)
        {
            return;
        }

        var appPath = Path.Combine(projectRoot, "App.xaml");
        if (!File.Exists(appPath))
        {
            errors.Add(Issue(
                "IntegrationAppXamlMissing",
                "Resource or startup integration requires an existing App.xaml under projectRoot.",
                "Create the WPF application shell, then rerun the dry-run plan."));
            return;
        }

        var namespaces = ResolveResourceNamespaces(selectedManifests, resources);
        var purposes = new List<string>();
        if (resources.Count > 0)
        {
            purposes.Add("resources");
        }

        if (setStartup)
        {
            purposes.Add("startup");
        }

        AddPatchedOperation(
            "application-xaml",
            appPath,
            purposes,
            ProjectIntegrationXmlPatcher.PatchApplication(
                appPath,
                projectRoot,
                targetPath,
                resources,
                namespaces,
                setStartup),
            "Merge pack-declared resources and select the generated window as StartupUri.",
            operations,
            errors);
    }

    private static void AddCodeBehindOperation(
        string appliedXaml,
        CodeBehindIntegrationPlan? codeBehind,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        if (codeBehind is null)
        {
            return;
        }

        if (!TryGetClassIdentity(appliedXaml, out var rootNamespace, out var className))
        {
            errors.Add(Issue(
                "IntegrationXClassMissing",
                "Pack-declared code-behind integration requires generated x:Class metadata.",
                "Use apply_ui_blueprint with a project target that can resolve x:Class, then rerun dry-run."));
            return;
        }

        AddPatchedOperation(
            "code-behind-base-type",
            codeBehind.TargetPath,
            ["code-behind"],
            ProjectIntegrationCodePatcher.Patch(
                codeBehind.TargetPath,
                rootNamespace,
                className,
                codeBehind.BaseType),
            $"Align the generated x:Class code-behind with pack-declared base type {codeBehind.BaseType}.",
            operations,
            errors);
    }

    private static void AddPatchedOperation(
        string role,
        string path,
        IReadOnlyList<string> purposes,
        ProjectContentPatchResult patch,
        string description,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        if (!patch.Success)
        {
            errors.Add(patch.Error!);
            return;
        }

        var exists = File.Exists(path);
        string current;
        try
        {
            current = exists ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(Issue(
                "IntegrationPreconditionReadFailed",
                $"Could not read integration target '{path}': {ex.Message}",
                "Resolve the file access issue, then rerun the dry-run plan."));
            return;
        }

        var currentHash = exists ? Sha256(current) : string.Empty;
        var proposedHash = Sha256(patch.Content);
        operations.Add(new ProjectIntegrationOperation(
            role,
            path,
            currentHash == proposedHash ? "none" : exists ? "update" : "create",
            purposes,
            new ProjectFilePrecondition(exists, currentHash),
            proposedHash,
            description,
            patch.Content));
    }

    private static IReadOnlyDictionary<string, string> ResolveResourceNamespaces(
        IReadOnlyList<UiPackManifest> manifests,
        IReadOnlyList<string> resources)
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            foreach (var item in manifest.XmlNamespaces)
            {
                if (resources.Any(resource => resource.Contains(item.Key + ":", StringComparison.Ordinal)))
                {
                    namespaces[item.Key] = item.Value;
                }
            }
        }

        return namespaces;
    }

    private static bool IsWindowRoot(IReadOnlyList<UiPackManifest> manifests, string xaml)
    {
        try
        {
            var root = XDocument.Parse(xaml).Root;
            if (root is null)
            {
                return false;
            }

            if (string.Equals(root.Name.LocalName, "Window", StringComparison.Ordinal))
            {
                return true;
            }

            return manifests
                .Where(manifest => string.Equals(
                    manifest.Preview?.NamespaceUri,
                    root.Name.NamespaceName,
                    StringComparison.Ordinal))
                .Any(manifest => manifest.Preview!.Types.TryGetValue(root.Name.LocalName, out var type)
                    && string.Equals(type.BaseKind, "window", StringComparison.Ordinal));
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static IReadOnlyList<UiPackManifest> ResolveSelectedManifests(
        PackRegistry registry,
        string blueprintJson)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            blueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var requested = new Dictionary<string, ComposerPackReference>(StringComparer.Ordinal);
        foreach (var pack in blueprint.Packs)
        {
            requested[pack.Id] = pack;
        }

        return registry.ListPacks().Packs
            .Where(available => requested.TryGetValue(available.Id, out var packRef)
                && string.Equals(packRef.Version, available.Version, StringComparison.Ordinal))
            .Select(available => ComposerPackLoader.Load(available.RootPath).Manifest)
            .ToArray();
    }

    private static bool TryGetClassIdentity(string xaml, out string rootNamespace, out string className)
    {
        rootNamespace = className = string.Empty;
        try
        {
            var value = XDocument.Parse(xaml).Root?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Class")?.Value;
            var separator = value?.LastIndexOf('.') ?? -1;
            if (separator <= 0 || separator == value!.Length - 1)
            {
                return false;
            }

            rootNamespace = value[..separator];
            className = value[(separator + 1)..];
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string? ResolveInsideRoot(
        string projectRoot,
        string relativePath,
        string role,
        List<ApplyBlueprintIssue> errors)
    {
        var path = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        if (ProjectWritePolicy.IsPathUnderRoot(projectRoot, path))
        {
            return path;
        }

        errors.Add(Issue(
            "IntegrationPathOutsideRoot",
            $"The inspected {role} is outside projectRoot and cannot be changed by guarded integration.",
            "Choose a projectRoot that contains every reviewed integration target."));
        return null;
    }

    private static string ComputePlanHash(IReadOnlyList<ProjectIntegrationOperation> operations)
    {
        var contract = operations.Select(operation => new
        {
            operation.Role,
            operation.TargetPath,
            operation.Action,
            operation.Purposes,
            operation.Precondition,
            operation.ProposedSha256
        });
        return Sha256(JsonSerializer.Serialize(contract));
    }

    internal static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static ApplyBlueprintIssue Issue(string code, string message, string repair)
        => new("$.projectRoot", code, message, repair);
}
