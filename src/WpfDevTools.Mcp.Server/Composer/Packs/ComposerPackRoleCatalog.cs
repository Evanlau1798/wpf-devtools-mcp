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

internal sealed record ComposerPackRolePlan(string Role, bool Required);

internal static class ComposerPackKindRoleResolver
{
    public static ComposerPackRolePlan Resolve(string packKind)
        => packKind switch
        {
            "style-pack" or "skill-generated-style-pack" => new(ComposerPackRoles.Primary, true),
            "control-pack" => new(ComposerPackRoles.ControlPack, false),
            "layout-pack" => new(ComposerPackRoles.LayoutPack, false),
            "icon-pack" => new(ComposerPackRoles.IconPack, false),
            "recipe-pack" => new(ComposerPackRoles.RecipePack, false),
            "extension-pack" => new(ComposerPackRoles.Extension, false),
            "project-local-pack" => new(ComposerPackRoles.ProjectLocalPack, false),
            _ => new(ComposerPackRoles.Other, false)
        };
}
