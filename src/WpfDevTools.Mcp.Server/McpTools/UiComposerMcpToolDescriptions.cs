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
        - includeRecipes=true includes recipe catalog entries that can be passed to expand_ui_recipe.

        EXAMPLES:
        - {"packIds":["wpfui"],"category":"navigation"}
        - {"kind":"wpfui.button"}
        - {"kindPrefix":"wpfui.navigation","composableOnly":true,"includeRecipes":true}
        """;

    public const string ValidateUiBlueprint =
        """
        Use this tool to validate a WPF DevTools Composer UI blueprint against installed pack contracts before rendering or applying generated XAML.

        CATEGORY: UI Composer

        USE WHEN: You have blueprint JSON and need pack resolution, block kind, slot, property type, enum, required property, and repair guidance checks.

        DO NOT USE: Do not use this for live WPF runtime inspection or mutation. Use connect and scene tools for a running target application.

        RESPONSE SUMMARY:
        - Returns success, valid, errorCount, warningCount, errors, warnings, and diagnostics.
        - Errors and warnings include jsonPath, code, message, repairSuggestion, and relevant allowedKinds or allowedValues.
        - valid=false is a validation result, not an MCP transport failure.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        - {"blueprintJson":"{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"name\":\"Demo\",\"packs\":[{\"id\":\"wpfui\",\"version\":\"0.1.0\"}],\"primaryPack\":\"wpfui\",\"layout\":{\"kind\":\"wpfui.button\"}}"}
        """;

    public const string ExpandUiRecipe =
        """
        Use this tool to expand a WPF DevTools Composer starter recipe into a blueprint and immediately validate the expanded result.

        CATEGORY: UI Composer

        USE WHEN: You want to start from a known UI pattern, such as a navigation shell, dashboard card, data grid page, dialog flow, or tabbed settings view, instead of authoring a blueprint from scratch.

        DO NOT USE: Do not use this for rendering, writing files, or mutating a WPF project. Use render_ui_blueprint or apply_ui_blueprint in later guarded workflows.

        RESPONSE SUMMARY:
        - Returns success, valid, recipeId, blueprint, validation, errors, warnings, and diagnostics.
        - valid=false means expansion completed but the recipe or expanded blueprint did not pass validation.
        - The expanded blueprint is a full blueprint document with packs, primaryPack, layout, and metadata.

        REQUEST OPTIONS:
        - recipeId is required and must be a pack-qualified recipe id from get_ui_block_catalog(includeRecipes=true).
        - inputs optionally provides JSON values for recipe inputs; omitted inputs use recipe defaults when available.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        - {"recipeId":"wpfui.shellWithNavigation"}
        - {"recipeId":"wpfui.dataGridPage","inputs":{"itemsSource":"{Binding Orders}","emptyText":"No orders"}}
        """;

    public const string RenderUiBlueprint =
        """
        Use this tool to render a WPF DevTools Composer UI blueprint to XAML in dry-run mode.

        CATEGORY: UI Composer

        USE WHEN: You have a validated blueprint or expanded recipe and need generated XAML plus a file plan before any guarded apply workflow.

        DO NOT USE: Do not use this to write project files. This tool always returns a dry-run plan and does not modify the filesystem.

        RESPONSE SUMMARY:
        - Returns success, valid, dryRun, xaml, filePlan, requiredResources, requiredNuGetPackages, validation, errors, and diagnostics.
        - Renderer errors include jsonPath and block/template-oriented recovery guidance.
        - filePlan.wouldWriteFiles is always false.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - targetPath optionally supplies a target XAML path suggestion; the renderer does not write it.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        - {"blueprintJson":"{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"name\":\"Demo\",\"packs\":[{\"id\":\"wpfui\",\"version\":\"0.1.0\"}],\"primaryPack\":\"wpfui\",\"layout\":{\"kind\":\"wpfui.button\"}}"}
        """;

    public const string ApplyUiBlueprint =
        """
        Use this tool to produce a guarded apply plan for a WPF DevTools Composer UI blueprint.

        CATEGORY: UI Composer

        USE WHEN: You need a project file plan for generated XAML and want writes blocked unless explicit destructive and project-write gates are configured.

        DO NOT USE: Do not use this as a general filesystem writer. Non-dry-run writes require WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true, WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true, and an exact WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS match.

        RESPONSE SUMMARY:
        - Dry-run is the default and returns filePlan, resourcePlan, requiredNuGetPackages, and viewModelBindingContract without writing files.
        - Non-dry-run writes persist generated XAML, are restricted to targetPath under projectRoot, and create a backup when updating an existing view file.
        - Generated files include WPFDEVTOOLS_BLUEPRINT_SOURCE and WPFDEVTOOLS_SAFE_SLOT markers for reversible repair-first workflows.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - projectRoot is required and must be the reviewed local WPF project root.
        - targetPath optionally supplies the target XAML file path; it must stay inside projectRoot.
        - dryRun defaults to true. Set false only after reviewing the returned plan and configuring destructive plus project-write gates.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        - {"blueprintJson":"{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"name\":\"Demo\",\"packs\":[{\"id\":\"wpfui\",\"version\":\"0.1.0\"}],\"primaryPack\":\"wpfui\",\"layout\":{\"kind\":\"wpfui.button\"}}","projectRoot":"C:\\Work\\SampleApp"}
        """;

    public const string PreviewUiBlueprint =
        """
        Use this tool to compile generated UI Composer XAML in a temporary WPF preview project and optionally start the preview host.

        CATEGORY: UI Composer

        USE WHEN: You need compile-smoke diagnostics, or a temporary host load smoke, before applying generated XAML to a real project.

        DO NOT USE: Do not use this to launch or control a real target application. This phase builds a temporary local preview host, optionally starts that temporary host, and reports diagnostics; it does not persist project files outside its temporary workspace.

        RESPONSE SUMMARY:
        - Returns success, valid, buildSucceeded, restoreEnabled, buildOutput, xaml, diagnostics, and previewHost.
        - restoreEnabled=false runs dotnet build --no-restore and returns missing-assets diagnostics when the temporary project has not been restored.
        - startHost=true starts the temporary host after build, waits for an explicit generated-view load sentinel, then terminates the process tree.
        - Compile failures map back to $.layout and the root renderer template path when available.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - restoreEnabled defaults to true for compile smoke; set false to verify restore-disabled diagnostics.
        - startHost defaults to false for fast compile smoke; set true for preview host load smoke.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        - {"blueprintJson":"{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"name\":\"Demo\",\"packs\":[{\"id\":\"wpfui\",\"version\":\"0.1.0\"}],\"primaryPack\":\"wpfui\",\"layout\":{\"kind\":\"wpfui.button\"}}","restoreEnabled":true,"startHost":true}
        """;
}
