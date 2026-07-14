namespace WpfDevTools.Mcp.Server.Composer.Contracts;

internal static class UiComposerSchemaVersions
{
    public const string SourceRepository = "https://github.com/Evanlau1798/wpf-devtools-extension-pack-creator";
    public const string SourceRef = "master";
    public const string SourceCommit = "cafedf2945a5660f966b516e76027f4d4f44af9f";

    public const string UiPack = "wpfdevtools.ui-pack.v1";
    public const string UiBlock = "wpfdevtools.ui-block.v1";
    public const string UiRecipe = "wpfdevtools.ui-recipe.v1";
    public const string UiBlueprint = "wpfdevtools.ui-blueprint.v1";
    public const string SourceLock = "wpfdevtools.source-lock.v1";
    public const string PackInstallManifest = "wpfdevtools.pack-install-manifest.v1";
    public const string ComposerProject = "wpfdevtools.composer-project.v1";

    public static IReadOnlyDictionary<string, string> SchemaFiles { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UiPack] = "wpfdevtools.ui-pack.v1.schema.json",
            [UiBlock] = "wpfdevtools.ui-block.v1.schema.json",
            [UiRecipe] = "wpfdevtools.ui-recipe.v1.schema.json",
            [UiBlueprint] = "wpfdevtools.ui-blueprint.v1.schema.json",
            [SourceLock] = "wpfdevtools.source-lock.v1.schema.json",
            [PackInstallManifest] = "wpfdevtools.pack-install-manifest.v1.schema.json",
            [ComposerProject] = "wpfdevtools.composer-project.v1.schema.json"
        };
}
