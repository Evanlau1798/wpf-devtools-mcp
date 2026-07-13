namespace WpfDevTools.Mcp.Server.McpTools;

internal static class UiComposerMcpToolDescriptions
{
    public const string ImportUiBlockPack =
        """
        Import a normalized Composer block-pack archive into one reviewed project.

        CATEGORY: UI Composer

        USE WHEN: Installing a creator-produced project-local pack.

        DO NOT USE: This does not edit app project, resource, XAML, or code files.

        RESPONSE SUMMARY:
        - Returns the pack identity, hash, confined file plan, and import state.

        SAFETY: Defaults to dry-run. Writes require confirmation, project-write policy, and an allowlisted root; replacement is opt-in.

        EXAMPLES:
        """;

    public const string ListUiBlockPacks =
        """
        CATEGORY: UI Composer

        USE WHEN: You need to discover installed WPF DevTools Composer UI block packs before building a block catalog, validating a blueprint, or rendering generated XAML.

        DO NOT USE: Do not use this for live WPF runtime inspection. Use connect, get_ui_summary, and scene tools for a running target application.

        RESPONSE SUMMARY:
        - Returns success, packCount, packs, allowedPackRoles, and diagnostics.
        - Each pack entry includes id, version, scope, kind, themeTokens, role, required, blockCount, recipeCount, exampleCount, rendererCount, readinessValid, sourceRepository, and blockKinds.
        - role is the pack-kind-derived suggested blueprint role. required=true marks a default required declaration; every pack whose blocks are used must still be declared.
        - allowedPackRoles is the authoritative pack-neutral role list for blueprint packs[].role values.
        - The response omits absolute pack root paths; read structuredContent for the canonical payload and content[0].text only as a compact fallback.

        REQUEST OPTIONS:
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs. Omit it to use the current user's LocalApplicationData path when available.

        EXAMPLES:
        """;

    public const string GetUiBlockCatalog =
        """
        CATEGORY: UI Composer

        USE WHEN: You need available block kinds, per-block properties, slot composition rules, renderer availability, or source hints from installed Composer UI packs.

        DO NOT USE: Do not use this for live WPF runtime inspection or to read third-party source code. Use scene tools for running target state.

        RESPONSE SUMMARY:
        - Returns success, itemCount, items, authoringGuidance, and diagnostics.
        - Each item includes packId, packVersion, kind, displayName, pack-defined description, category, properties, slots, allowedKinds, rendererAvailable, compositionSkeleton, and sourceHintSummary.
        - Property and slot description fields explain renderer semantics. A property previewWarning identifies pack-defined final-app checks without executing pack code.
        - compositionSkeleton is a compact pack-neutral node fragment generated from that block's own required properties and declared slots. Copy it into a blueprint instead of retyping kind and slot names.
        - authoringGuidance keeps creative-brief selection independent from recipes. Recipes remain optional accelerators or fragments.
        - Catalog source hints are path summaries only and do not include copied third-party source text.

        REQUEST OPTIONS:
        - packIds optionally filters by pack id.
        - category optionally filters by block category.
        - kindPrefix optionally filters by block kind prefix.
        - composableOnly=true returns only blocks with available renderer templates.
        - kind optionally returns single-block detail for an exact pack-qualified block kind.
        - includeRecipes defaults to false for brief-first discovery. Set true only after choosing an independent creative brief.

        EXAMPLES:
        """;

    public const string ValidateUiBlueprint =
        """
        CATEGORY: UI Composer

        USE WHEN: You have blueprint JSON and need pack resolution, block kind, slot, property type, enum, required property, and repair guidance checks.

        DO NOT USE: Do not use this for live WPF runtime inspection or mutation. Use connect and scene tools for a running target application.

        RESPONSE SUMMARY:
        - Returns success, valid, errorCount, warningCount, errors, warnings, blueprintSize, and diagnostics.
        - Errors and warnings include jsonPath, code, message, repairSuggestion, and relevant allowedKinds or allowedValues.
        - blueprintSize reports currentCharacters, maximumCharacters, remainingCharacters, and utilizationPercent for the public blueprintJson limit.
        - valid=false is a validation result, not an MCP transport failure.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """;

    public const string ComposeUiBlueprint =
        """
        CATEGORY: UI Composer

        USE WHEN: You have a current blueprint and need to append or insert a catalog block into an existing slot.

        DO NOT USE: Do not use this as a general JSON patch tool or filesystem writer. It only inserts exact compositionSkeleton content declared by installed packs and validates the candidate blueprint before returning it.

        RESPONSE SUMMARY:
        - Returns success, composed, blueprint, blueprintJson, insertedPath, validation, and errors.
        - composed=true returns a new validated blueprint object; the input text is never mutated and no file is written.
        - composed=false omits the candidate blueprint and returns path or pack-validation errors with repair guidance.

        REQUEST OPTIONS:
        - blueprintJson is the current full blueprint JSON text.
        - targetPath identifies an existing slot. Use $.layout.slots.<slot> for a root slot or include an explicit child index before each nested slot.
        - kind is an exact pack-qualified kind from get_ui_block_catalog(composableOnly=true).
        - insertionIndex optionally inserts before an existing child; omit it to append.

        EXAMPLES:
        """;

    public const string ExpandUiRecipe =
        """
        CATEGORY: UI Composer

        USE WHEN: You already chose an independent creative brief and want a pack-defined recipe as an optional accelerator or fragment instead of assembling every initial node manually.

        DO NOT USE: Do not use this for rendering, writing files, or mutating a WPF project. Use render_ui_blueprint or apply_ui_blueprint in later guarded workflows.

        RESPONSE SUMMARY:
        - Returns success, valid, recipeId, blueprint, validation, errors, warnings, and diagnostics.
        - valid=false means expansion completed but the recipe or expanded blueprint did not pass validation.
        - The expanded blueprint is a full blueprint document with packs, primaryPack, layout, and metadata.
        - Serialize the returned blueprint object to JSON text and pass it as blueprintJson to validation, render, preview, repair, or apply; do not rename the parameter to blueprint.

        REQUEST OPTIONS:
        - recipeId is required and must be a pack-qualified recipe id from get_ui_block_catalog(includeRecipes=true).
        - inputs optionally provides JSON values for recipe inputs; omitted inputs use recipe defaults when available.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """;

    public const string RenderUiBlueprint =
        """
        CATEGORY: UI Composer

        USE WHEN: You have a validated blueprint or expanded recipe and need generated XAML plus a file plan before any guarded apply workflow.

        DO NOT USE: Do not use this to write project files. This tool always returns a dry-run plan and does not modify the filesystem.

        RESPONSE SUMMARY:
        - Returns success, valid, dryRun, xaml, filePlan, requiredResources, requiredNuGetPackages, packageIntegrationGuidance, validation, errors, and diagnostics.
        - packageIntegrationGuidance inspects the target project and emits generic projectPackageReference plus optional centralPackageVersion snippets without editing files.
        - Renderer errors include jsonPath and block/template-oriented recovery guidance.
        - filePlan.wouldWriteFiles is always false.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - targetPath optionally supplies a target XAML path suggestion; the renderer does not write it.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """;

    public const string RepairUiBlueprint =
        """
        CATEGORY: UI Composer

        USE WHEN: validate_ui_blueprint, render_ui_blueprint, or preview_ui_blueprint reported issues and you need the next safe blueprint or pack-contract repair step.

        DO NOT USE: Do not use this to patch generated XAML. This tool returns repair actions only and does not write files.

        RESPONSE SUMMARY:
        - Returns success, repairable, generatedXamlPatch=false, actionCount, actions, and diagnostics.
        - Actions identify source, target, repairKind, issueCode, jsonPath, message, suggestedAction, allowedKinds, allowedValues, optional suggestedValue, and optional rendererTemplatePath.
        - Validation repairs prefer blueprint changes such as choosing catalog blocks, replacing forbidden child kinds, adding required properties, or importing missing packs.
        - Renderer and compile repairs identify whether the next step is a blueprint change or a pack renderer template issue.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - diagnosticsJson optionally accepts diagnostics returned by render_ui_blueprint or preview_ui_blueprint.
        - targetPath optionally supplies a target XAML path suggestion for render diagnostics only; the tool does not write it.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """;

    public const string ApplyUiBlueprint =
        """
        CATEGORY: UI Composer

        USE WHEN: You need a project file plan for generated XAML and want writes blocked unless explicit destructive and project-write gates are configured.

        DO NOT USE: Do not use this as a general filesystem writer. Non-dry-run writes require WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true, WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true, and an exact WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS match.

        RESPONSE SUMMARY:
        - Dry-run is the default and returns filePlan entries with targetPath, action, riskLevel, resourcePlan, requiredNuGetPackages, packageIntegrationGuidance, viewModelBindingContract, and behaviorIntegrationContract without writing files.
        - packageIntegrationGuidance reports ManagePackageVersionsCentrally detection, inspectionConfidence, and inspectionReason. It returns snippets only for detected project or central modes; mode=unknown omits package snippets and requires project inspection. Composer does not edit project or central package files.
        - behaviorIntegrationContract preserves every declared command interaction with bindingStatus, raw commandBinding, nullable commandPath, parameter, and verification guidance. Resolve complex bindings before treating controls as functional.
        - Non-dry-run writes require confirmApply=true, persist generated XAML atomically, are restricted to project-root-relative targetPath under projectRoot, and create a backup when updating an existing view file.
        - A successful non-dry-run response returns the executed file plan using the pre-write target state: create remains create and update includes its backup path.
        - Root blocks whose pack renderer declares codeBehindBaseType add x:Class and return a code-behind-integration filePlan entry for that validated pack-defined base type.
        - Generated files include WPFDEVTOOLS_BLUEPRINT_SOURCE and WPFDEVTOOLS_SAFE_SLOT markers for reversible repair-first workflows.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - projectRoot is required and must be the reviewed local WPF project root.
        - targetPath optionally supplies a project-root-relative generated view XAML path; absolute paths, .git, App.xaml, project files, ResourceDictionary folders, and ViewModel paths are blocked by default.
        - dryRun defaults to true. Set false only after reviewing the returned plan and configuring destructive plus project-write gates.
        - confirmApply defaults to false and must be true for non-dry-run writes after reviewing the dry-run file plan.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """;

    public const string PreviewUiBlueprint =
        """
        CATEGORY: UI Composer

        USE WHEN: You need compile-smoke diagnostics, a temporary host load smoke, or scene/layout runtime diagnostics before applying generated XAML to a real project.

        DO NOT USE: Do not use this to launch or control a real target application. This phase builds a temporary local preview host, optionally starts that temporary host, and reports diagnostics; it does not persist project files outside its temporary workspace.

        RESPONSE SUMMARY:
        - Returns success, valid, buildSucceeded, restoreEnabled, buildOutput, xaml, diagnostics, previewHost, visualFidelity, visualValidationGuidance, visualComparisonChecklist, propertyWarnings, and elementCorrelations.
        - visualFidelity="structural-stub" means preview screenshots are structural-only evidence. Validate final styling in the applied, built, and launched WPF application.
        - visualComparisonChecklist names expected stub differences in window chrome, icons, control templates, and layout and spacing, with a required final-app check for each area.
        - propertyWarnings contains pack-defined guidance only for explicitly supplied properties, with the exact blueprint JSON path, block kind, property name, and message.
        - elementCorrelations maps each renderer root's transient x:Name to its blueprint jsonPath and blockKind. These names are never written into the blueprint or emitted by render/apply.
        - restoreEnabled=false runs dotnet build --no-restore and returns missing-assets diagnostics when the temporary project has not been restored.
        - startHost=true starts the temporary host after build, waits for an explicit generated-view load sentinel, then terminates the process tree.
        - includeRuntimeDiagnostics=true with startHost=true reuses connect(), semantic summary, a bounded correlation lookup plan covering generated and renderer-provided names, and layout diagnostics.
        - includeScreenshotDiagnostics=true with startHost=true enables runtime diagnostics and adds element_screenshot using screenshotOutputMode only when the screenshot policy gate allows it.
        - screenshotOutputMode="file" returns a resource-backed PNG that remains readable after the temporary preview host exits.
        - Compile failures map back to the compiler line/column source-map entry and renderer template path when available; restore/build infrastructure failures stay at $.layout.

        REQUEST OPTIONS:
        - blueprintJson is required and must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - restoreEnabled defaults to true for compile smoke; set false to verify restore-disabled diagnostics.
        - startHost defaults to false for fast compile smoke; set true for preview host load smoke.
        - includeRuntimeDiagnostics defaults to false; set true with startHost=true after enabling the sensitive-reads policy gate.
        - includeScreenshotDiagnostics defaults to false; set true with startHost=true only when pixel evidence is needed; it requires both sensitive-reads and screenshot policy gates.
        - screenshotOutputMode defaults to metadata; use file when resources/read structural pixel evidence is required. Do not approve final styling from the preview image.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """;
}
