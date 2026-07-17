using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ComposerWindowRootResolver
{
    public static bool IsWindowRoot(PackRegistry registry, string blueprintJson, string xaml)
        => IsWindowRoot(ResolveSelectedManifests(registry, blueprintJson), xaml);

    public static bool IsWindowRoot(IReadOnlyList<UiPackManifest> manifests, string xaml)
        => string.Equals(ResolveRootBaseKind(manifests, xaml), "window", StringComparison.Ordinal);

    public static string? ResolveRootBaseKind(PackRegistry registry, string blueprintJson, string xaml)
        => ResolveRootBaseKind(ResolveSelectedManifests(registry, blueprintJson), xaml);

    public static string? ResolveRootBaseKind(IReadOnlyList<UiPackManifest> manifests, string xaml)
    {
        try
        {
            var root = XDocument.Parse(xaml).Root;
            if (root is null)
            {
                return null;
            }

            if (string.Equals(root.Name.LocalName, "Window", StringComparison.Ordinal))
            {
                return "window";
            }

            if (string.Equals(root.Name.NamespaceName, "http://schemas.microsoft.com/winfx/2006/xaml/presentation", StringComparison.Ordinal))
            {
                return root.Name.LocalName switch
                {
                    "ResourceDictionary" => "resourceDictionary",
                    "UserControl" or "Page" => "contentControl",
                    "Grid" or "StackPanel" or "DockPanel" or "Canvas" or "WrapPanel" or "UniformGrid" => "stackPanel",
                    _ => null
                };
            }

            var declaredBaseKinds = manifests
                .Where(manifest => string.Equals(
                    manifest.Preview?.NamespaceUri,
                    root.Name.NamespaceName,
                    StringComparison.Ordinal))
                .Select(manifest => manifest.Preview!.Types.GetValueOrDefault(root.Name.LocalName)?.BaseKind)
                .Where(baseKind => !string.IsNullOrWhiteSpace(baseKind))
                .ToArray();
            return declaredBaseKinds.Contains("window", StringComparer.Ordinal)
                ? "window"
                : declaredBaseKinds.FirstOrDefault();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public static IReadOnlyList<UiPackManifest> ResolveSelectedManifests(
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
}
