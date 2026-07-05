using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.McpTools;

[McpServerToolType]
public static class UiComposerMcpTools
{
    [McpServerTool(Name = "list_ui_block_packs", Title = "List UI Composer Block Packs", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(UiComposerMcpToolDescriptions.ListUiBlockPacks)]
    public static Task<CallToolResult> ListUiBlockPacks(
        [Description("Optional local WPF project root. When provided, discovers project-local packs from <projectRoot>/.wpfdevtools/packs before user-global and built-in packs.")] string? projectRoot = null,
        [Description("Optional LocalApplicationData root override for user-global packs. Omit to use the current user's LocalApplicationData path when available.")] string? localAppDataRoot = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("projectRoot", projectRoot),
            ("localAppDataRoot", localAppDataRoot));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(ListPacks(projectRoot, localAppDataRoot)),
            args,
            cancellationToken,
            timeoutSeconds: 10);
    }

    private static object ListPacks(string? projectRoot, string? localAppDataRoot)
    {
        var composerRoot = ComposerRuntimePaths.ResolveComposerRoot();
        var registry = new PackRegistry(
            ComposerPackPaths.BuiltinRoot(composerRoot),
            ResolveProjectPackRoot(projectRoot),
            ResolveUserPackRoot(localAppDataRoot));
        var result = registry.ListPacks();

        return new
        {
            success = true,
            packCount = result.Packs.Count,
            packs = result.Packs.Select(ToPayload).ToArray(),
            diagnostics = result.Diagnostics
        };
    }

    private static object ToPayload(PackRegistryItem pack)
        => new
        {
            pack.Id,
            pack.Version,
            scope = ToScopeName(pack.Scope),
            pack.BlockCount,
            pack.RecipeCount,
            pack.ExampleCount,
            pack.RendererCount,
            pack.ReadinessValid,
            pack.SourceRepository,
            pack.BlockKinds
        };

    private static string? ResolveProjectPackRoot(string? projectRoot)
        => string.IsNullOrWhiteSpace(projectRoot)
            ? null
            : ComposerPackPaths.ProjectLocalRoot(projectRoot);

    private static string? ResolveUserPackRoot(string? localAppDataRoot)
    {
        var root = string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataRoot;
        return string.IsNullOrWhiteSpace(root)
            ? null
            : ComposerPackPaths.UserGlobalRoot(root);
    }

    private static string ToScopeName(PackScope scope)
        => scope switch
        {
            PackScope.ProjectLocal => "project-local",
            PackScope.UserGlobal => "user-global",
            PackScope.Builtin => "built-in",
            _ => "unknown"
        };
}
