using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ComposerWindowRootResolver
{
    public static bool IsWindowRoot(PackRegistry registry, string blueprintJson, string xaml)
        => IsWindowRoot(ResolveSelectedManifests(registry, blueprintJson), xaml);

    public static bool IsWindowRoot(IReadOnlyList<UiPackManifest> manifests, string xaml)
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
