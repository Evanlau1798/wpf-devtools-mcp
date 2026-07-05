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

    public const string GetUiBlockCatalog =
        """
        Use this tool to inspect WPF DevTools Composer block definitions, properties, slots, allowedKinds, renderer availability, and source provenance before composing a blueprint.

        CATEGORY: UI Composer

        USE WHEN: You need available block kinds, per-block properties, slot composition rules, renderer availability, or source hints from installed Composer UI packs.

        DO NOT USE: Do not use this for live WPF runtime inspection or to read third-party source code. Use scene tools for running target state.

        RESPONSE SUMMARY:
        - Returns success, itemCount, items, and diagnostics.
        - Each item includes packId, packVersion, kind, displayName, category, properties, slots, allowedKinds, rendererAvailable, and sourceHintSummary.
        - Catalog source hints are path summaries only and do not include copied third-party source text.

        REQUEST OPTIONS:
        - packIds optionally filters by pack id.
        - category optionally filters by block category.
        - kindPrefix optionally filters by block kind prefix.
        - composableOnly=true returns only blocks with available renderer templates.
        - kind optionally returns single-block detail for an exact pack-qualified block kind.

        EXAMPLES:
        - {"packIds":["wpfui"],"category":"navigation"}
        - {"kind":"wpfui.button"}
        - {"kindPrefix":"wpfui.navigation","composableOnly":true}
        """;
}
