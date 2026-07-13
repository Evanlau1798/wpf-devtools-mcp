using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class CodeBehindIntegrationResolver
{
    public static CodeBehindIntegrationPlan? Resolve(
        PackRegistry registry,
        string blueprintJson,
        string targetPath)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            blueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var declaredIds = blueprint.Packs.Select(pack => pack.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        var packId = ComposerPackKindResolver.ResolveDeclaredPackId(blueprint.Layout.Kind, declaredIds);
        if (packId is null)
        {
            return null;
        }

        var registryItem = registry.ListPacks().Packs.FirstOrDefault(pack =>
            string.Equals(pack.Id, packId, StringComparison.Ordinal));
        if (registryItem is null)
        {
            return null;
        }

        var block = ComposerPackLoader.Load(registryItem.RootPath).Blocks.FirstOrDefault(candidate =>
            string.Equals(candidate.Kind, blueprint.Layout.Kind, StringComparison.Ordinal));
        var baseType = block?.Renderer.CodeBehindBaseType;
        return string.IsNullOrWhiteSpace(baseType)
            ? null
            : new CodeBehindIntegrationPlan(Path.ChangeExtension(targetPath, ".xaml.cs"), baseType);
    }
}

internal sealed record CodeBehindIntegrationPlan(string TargetPath, string BaseType);
