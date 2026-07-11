namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerPackRoles
{
    public const string Primary = "primary";
    public const string Extension = "extension";
    public const string ControlPack = "control-pack";
    public const string IconPack = "icon-pack";
    public const string LayoutPack = "layout-pack";
    public const string RecipePack = "recipe-pack";
    public const string ProjectLocalPack = "project-local-pack";
    public const string Other = "other";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Primary,
        Extension,
        ControlPack,
        IconPack,
        LayoutPack,
        RecipePack,
        ProjectLocalPack,
        Other
    };
}

internal sealed record ComposerPackRolePlan(string Id, string Role, bool Required);

internal static class ComposerPackRoleCatalog
{
    public const string DefaultWpfUiPackId = "wpfui";

    public static IReadOnlyList<ComposerPackRolePlan> WpfUiPacks { get; } =
    [
        new(DefaultWpfUiPackId, ComposerPackRoles.Primary, true),
        new("wpfui.gallery", ComposerPackRoles.RecipePack, false),
        new("wpfui.syntaxhighlight", ComposerPackRoles.ControlPack, false),
        new("wpfui.tray", ComposerPackRoles.ControlPack, false),
        new("wpfui.templates", ComposerPackRoles.RecipePack, false)
    ];
}
