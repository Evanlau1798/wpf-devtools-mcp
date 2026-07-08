namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerPackRoles
{
    public const string Primary = "primary";
    public const string RecipeExample = "recipe-example";
    public const string OptionalControl = "optional-control";
    public const string ShellTemplate = "shell-template";
}

internal sealed record ComposerPackRolePlan(string Id, string Role, bool Required);

internal static class ComposerPackRoleCatalog
{
    public const string DefaultWpfUiPackId = "wpfui";

    public static IReadOnlyList<ComposerPackRolePlan> WpfUiPacks { get; } =
    [
        new(DefaultWpfUiPackId, ComposerPackRoles.Primary, true),
        new("wpfui.gallery", ComposerPackRoles.RecipeExample, false),
        new("wpfui.syntaxhighlight", ComposerPackRoles.OptionalControl, false),
        new("wpfui.tray", ComposerPackRoles.OptionalControl, false),
        new("wpfui.templates", ComposerPackRoles.ShellTemplate, false)
    ];
}
