namespace WpfDevTools.Mcp.Server.McpTools;

internal static class UiComposerMcpToolDescriptions
{
    public const string ListUiBlockPacks =
        """
        Use this tool to discover installed WPF DevTools Composer UI block packs before catalog, blueprint validation, rendering, or apply workflows.

        CATEGORY: UI Composer

        USE WHEN: You need to discover installed WPF DevTools Composer UI block packs before building a block catalog, validating a blueprint, or rendering generated XAML.

        DO NOT USE: Do not use this for live WPF runtime inspection. Use connect, get_ui_summary, and scene tools for a running target application.

        RESPONSE SUMMARY:
        - Returns success, packCount, packs, and diagnostics.
        - Each pack entry includes id, version, scope, blockCount, recipeCount, exampleCount, rendererCount, readinessValid, sourceRepository, and blockKinds.
        - The response omits absolute pack root paths; read structuredContent for the canonical payload and content[0].text only as a compact fallback.

        REQUEST OPTIONS:
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs. Omit it to use the current user's LocalApplicationData path when available.

        EXAMPLES:
        - {"projectRoot":"C:\\repo\\MyWpfApp"}
        - {"localAppDataRoot":"C:\\Users\\me\\AppData\\Local"}
        """;
}
