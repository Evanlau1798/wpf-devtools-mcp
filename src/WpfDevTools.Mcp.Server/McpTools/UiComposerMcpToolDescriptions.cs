namespace WpfDevTools.Mcp.Server.McpTools;

internal static class UiComposerMcpToolDescriptions
{
    private const string CanonicalExamples = "\nSee list_ui_block_packs.";
    private const string CanonicalExamplesIndex =
        "\nCanonical examples: https://wpf-mcptools.evanlau1798.com/reference/tools/ui-composer.html";

    public const string CreateUiBlueprintDraft =
        """
        USE WHEN: Reusing one blueprint across several Composer calls without retransmitting it.

        CATEGORY: UI Composer

        RETURNS: An immutable, bounded, process-local, temporary draftRef plus retention metadata; no blueprintJson echo.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string PatchUiBlueprintDraft =
        """
        USE WHEN: Applying JSON Merge Patch, one JSON-path set/remove, or up to 16 ordered atomic operations. jsonPath may be @Panel.properties.text. Do not combine patchJson, jsonPath, or operations.

        CATEGORY: UI Composer

        RETURNS: New immutable draftRef and bounded changeSummary; atomic entries include operationIndex. No blueprint echo. Use compose_ui_blueprint for slot insertion.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string ImportUiBlockPack =
        """
        Import a normalized Composer block-pack archive into one reviewed project.

        CATEGORY: UI Composer

        USE WHEN: Installing a creator-produced project-local pack.

        DO NOT USE: This does not edit app project, resource, XAML, or code files.

        RESPONSE SUMMARY:
        - Returns the pack identity, hash, confined file plan, and import state.

        SAFETY: Defaults to dry-run. Writes require its reviewed archiveSha256, confirmation, project-write policy, and an allowlisted root; replacement is opt-in.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string ListUiBlockPacks =
        """
        USE WHEN: You need to discover installed WPF DevTools Composer UI block packs before building a block catalog, validating a blueprint, or rendering generated XAML.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this for live WPF runtime inspection. Use connect, get_ui_summary, and scene tools for a running target application.

        RESPONSE SUMMARY:
        - Returns success, packCount, packs, allowedPackRoles, and diagnostics.
        - Each pack entry includes id, version, scope, kind, themeTokens, resourceVariants, role, required, blockCount, recipeCount, exampleCount, rendererCount, readinessValid, sourceRepository, and blockKinds.
        - resourceVariants lists pack-owned variants and appearances; select one in the blueprint without library-specific inference.
        - role is the pack-kind-derived suggested blueprint role. required=true marks a default required declaration; every pack whose blocks are used must still be declared.
        - allowedPackRoles is the authoritative pack-neutral role list for blueprint packs[].role values.
        - The response omits absolute pack root paths; read structuredContent for the canonical payload and content[0].text only as a compact fallback.

        REQUEST OPTIONS:
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs. Omit it to use the current user's LocalApplicationData path when available.

        EXAMPLES:
        """ + CanonicalExamplesIndex;

    public const string GetUiBlockCatalog =
        """
        USE WHEN: You need pack-defined block kinds, properties, slots, renderers, or source hints before authoring a blueprint.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this for live target inspection or third-party source retrieval.

        RESPONSE SUMMARY:
        - Full mode returns kind, description, previewWarning, authoring roles, pack guidance, properties, slots with declared bounds, renderer availability, skeleton, and source hints.
        - compact=true returns only discovery fields: identity, block description, category, property names and warnings, slot bounds, renderer availability, skeleton, and authoring roles; omitted maxItems means unbounded.
        - Large vocabularies return bounded matches plus total/match counts and truncation.
        - compositionSkeleton is a compact pack-neutral node derived from required properties and declared slots.
        - authoringGuidance keeps brief-first creative decisions independent; recipes remain optional accelerators.
        - Source hints never copy third-party source text.

        REQUEST OPTIONS:
        - Filter with packIds, category, kindPrefix, composableOnly, or exact kind.
        - Use compact=true broadly; use exact kind, compact=false, and optional allowedValueQuery for detail.
        - includeRecipes defaults to false; enable it only after choosing an independent brief.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string ValidateUiBlueprint =
        """
        USE WHEN: You have blueprint JSON and need pack resolution, block kind, slot, property type, enum, required property, and repair guidance checks.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this for live WPF runtime inspection or mutation. Use connect and scene tools for a running target application.

        RESPONSE SUMMARY:
        - Returns success, valid, errorCount, warningCount, errors, warnings, compositionMap, blueprintSize, and diagnostics.
        - Issues include path/code/repair and allowed values. InvalidBlueprintShape adds observedValueKind/expectedJsonShape; SurfaceThemeContrastRisk flags resource conflicts.
        - Optional node elementName and automationId values are validated for safe syntax, uniqueness, and generated class/member collisions before render.
        - compositionMap: up to 64 copy-ready slot targets with counts and capacity.
        - blueprintSize reports currentCharacters, maximumCharacters, remainingCharacters, and utilizationPercent for the public blueprintJson limit.
        - valid=false is a validation result, not an MCP transport failure.

        REQUEST OPTIONS:
        - blueprintJson accepts raw JSON or an opaque draftRef. Raw JSON must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - targetPath optionally checks generated class/member collisions for that XAML filename; omission uses Views/<blueprint-name>.xaml.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string ComposeUiBlueprint =
        """
        USE WHEN: You have a current blueprint and need to append or insert a catalog block into an existing slot.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this as a general JSON patch tool or filesystem writer. It only inserts exact compositionSkeleton content declared by installed packs and validates the candidate blueprint before returning it.

        RESPONSE SUMMARY:
        - Success returns validation plus the raw blueprint or a derived draftRef that omits the full blueprint; source drafts stay unchanged.
        - insertedNodeSummary: path/kind/elementName/automationId plus up to 32 properties with compact values capped at 160 characters and explicit truncation metadata.
        - targetSlotSummary: exact parent/slot, declared kinds/bounds, counts, and remaining capacity.
        - Failure returns success=false as an MCP error result with diagnostics and any available candidateDraftRef or candidateBlueprintJson.

        REQUEST OPTIONS:
        - blueprintJson accepts raw JSON or an opaque draftRef.
        - targetPath example: @Panel.slots.actions (or exact JSON path).
        - kind is an exact pack-qualified kind from get_ui_block_catalog(composableOnly=true).
        - elementName and automationId optionally assign validated, blueprint-wide unique standard identity.
        - properties optionally configures the inserted node with one JSON object of pack-defined values; installed block validation remains authoritative.
        - insertionIndex optionally inserts before an existing child; omit it to append.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string ExpandUiRecipe =
        """
        USE WHEN: You already chose an independent creative brief and want a pack-defined recipe as an optional accelerator or fragment instead of assembling every initial node manually.

        CATEGORY: UI Composer

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
        """ + CanonicalExamples;

    public const string RenderUiBlueprint =
        """
        USE WHEN: You have a validated blueprint or expanded recipe and need generated XAML plus a file plan before any guarded apply workflow.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this to write project files. This tool always returns a dry-run plan and does not modify the filesystem.

        RESPONSE SUMMARY:
        - Returns success, valid, dryRun, xaml, filePlan, requiredResources, requiredNuGetPackages, packageIntegrationGuidance, validation, errors, and diagnostics.
        - packageIntegrationGuidance inspects the target project and emits generic projectPackageReference plus optional centralPackageVersion snippets without editing files.
        - Renderer errors include jsonPath and block/template-oriented recovery guidance.
        - filePlan.wouldWriteFiles is always false.

        REQUEST OPTIONS:
        - blueprintJson accepts raw JSON or an opaque draftRef. Raw JSON must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - targetPath optionally supplies a target XAML path suggestion; the renderer does not write it.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string RepairUiBlueprint =
        """
        USE WHEN: validate_ui_blueprint, render_ui_blueprint, or preview_ui_blueprint reported issues and you need the next safe blueprint or pack-contract repair step.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this to patch generated XAML. This tool returns repair actions only and does not write files.

        RESPONSE SUMMARY:
        - Returns success, repairable, generatedXamlPatch=false, actionCount, actions, and diagnostics.
        - Actions identify source, target, repairKind, issueCode, jsonPath, message, suggestedAction, allowedKinds, allowedValues, optional suggestedValue, and optional rendererTemplatePath.
        - Validation repairs prefer blueprint changes such as choosing catalog blocks, replacing forbidden child kinds, adding required properties, or importing missing packs.
        - Renderer and compile repairs identify whether the next step is a blueprint change or a pack renderer template issue.

        REQUEST OPTIONS:
        - blueprintJson accepts raw JSON or an opaque draftRef. Raw JSON must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - diagnosticsJson optionally accepts diagnostics returned by render_ui_blueprint or preview_ui_blueprint.
        - targetPath optionally supplies a target XAML path suggestion for render diagnostics only; the tool does not write it.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string ApplyUiBlueprint =
        """
        USE WHEN: You need a dry-run or guarded write for generated XAML.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this as a general filesystem writer. Non-dry-run writes require WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true, WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true, and an exact WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS match.

        RESPONSE SUMMARY:
        - Dry-run returns filePlan, resourcePlan, requiredNuGetPackages, viewModelBindingContract, behaviorIntegrationContract, targetWindowPlan, and deterministic pack-neutral projectIntegrationPlan.
        - Apply omits XAML; opt in or call render_ui_blueprint.
        - packageIntegrationGuidance uses static XML best-effort ManagePackageVersionsCentrally detection. It reports inspectionConfidence, inspectedFiles, and inspectionLimitations; mode=unknown omits package snippets. This tool does not edit project or central package files.
        - Non-dry-run writes need confirmApply=true, are atomic under projectRoot, and return the executed file plan with pre-write state and backups.
        - Existing Window XAML hosts a non-Window root; targetWindowPlan reports copied preview dimensions. Pack codeBehindBaseType controls x:Class; reapply keeps one source header and safe-slot envelope.

        REQUEST OPTIONS:
        - Pass blueprintJson as raw JSON or an opaque draftRef, plus the exact reviewed projectRoot and an optional project-relative targetPath.
        - dryRun defaults true; confirm only after review. Copy preview dimensions to targetWindowWidth/targetWindowHeight; omit to preserve. localAppDataRoot selects the pack scope.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string ApplyUiProjectIntegration =
        """
        USE WHEN: Apply exact reviewed pack-neutral projectIntegrationPlan from apply_ui_blueprint.

        CATEGORY: UI Composer

        DO NOT USE: Never guess or reuse a stale hash. Requires WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true, WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true, and exact WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS.

        RESPONSE SUMMARY:
        - Regenerates the plan; stale reviewedPlanHash fails with IntegrationPlanChanged before writes.
        - Applies only generated package, App.xaml, startup, and code-behind operations under projectRoot.
        - Changes report backupPath and rollbackAction; failure rolls back earlier operations.
        - Package changes set packageRestoreRequired=true and buildGuidance for restore before --no-restore build.

        REQUEST OPTIONS:
        - blueprintJson is raw JSON or an opaque draftRef; match projectRoot, targetPath, reviewedPlanHash, confirmIntegration=true, and pack scope to the dry run.

        EXAMPLES:
        """ + CanonicalExamples;

    public const string PreviewUiBlueprint =
        """
        USE WHEN: You need compile, temporary host-load, or scene/layout evidence before applying generated XAML.

        CATEGORY: UI Composer

        DO NOT USE: Do not use this to control a real target. It builds an isolated temporary host and does not persist project files.

        RESPONSE SUMMARY:
        - Returns compile, host, visual, screenshot, warning, and correlation evidence.
        - visualFidelity is resource-backed, hybrid-resource-backed, structural, or not-available; verify the applied, built, and launched app.
        - Project/user packs stay structural until approved. runtimePackApprovalReviews supplies identity, fingerprint, eligibility, package hashes, and a content-bound approval token. For one preview call pass an eligible runtimePackApprovalTokens value; for server approval use WPFDEVTOOLS_COMPOSER_TRUSTED_RUNTIME_PACKS after enabling WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS=true. Packages require exact [version] and SHA-512 contentHash; a preview-local NuGet cache is hash-checked before build.
        - screenshotVerificationGuidance instructs the client to re-read the resource and verify SHA-256 before replacing a sparse but semantically complete preview.
        - visualComparisonChecklist lists final checks for window chrome, icons, control templates, layout and spacing.
        - propertyWarnings gives pack guidance for supplied properties with exact blueprint JSON path, block kind, property name, and message.
        - elementCorrelations maps transient x:Name to jsonPath/blockKind; names are never written into the blueprint or render/apply output.
        - layoutRiskSummary maps clipping, including Window client overflow, to jsonPath/blockKind; unresolved reasons are ambiguous-authored-name, lookup-budget, runtime-match-ambiguous, runtime-not-found, or search-incomplete.
        - Compile failures map to source line/column and renderer path when available; infrastructure failures stay at $.layout.

        REQUEST OPTIONS:
        - blueprintJson accepts raw JSON or an opaque draftRef. Raw JSON must contain schemaVersion wpfdevtools.ui-blueprint.v1.
        - restoreEnabled defaults to true for compile smoke; set false to verify restore-disabled diagnostics.
        - startHost defaults to false for compile smoke; set true for preview host load smoke.
        - includeRuntimeDiagnostics defaults to false; set true with startHost=true after enabling the sensitive-reads policy gate.
        - compactRuntimeDiagnostics is compact by default: XAML and risk-free correlations become counts; failures, risky correlations, and screenshot resource handles remain. False returns full payloads.
        - correlationLookupLimit caps authored elementName and renderer-provided root x:Name queries at 32 (max 64); raise only for lookup-budget.
        - Screenshots require startHost, sensitive-read, and screenshot gates.
        - Use screenshotOutputMode="file" for pixel evidence. Preview pixels do not approve final styling.
        - viewportWidth and viewportHeight set preview Window.Width and Window.Height in DIPs; match the target Window dimensions to expose overflow before apply. Screenshot bounds only resize returned pixels.
        - runtimePackApprovalTokens accepts up to 16 reviewed exact-content tokens for this call only; it requires WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS=true and is never persisted.
        - projectRoot optionally enables project-local discovery from <projectRoot>/.wpfdevtools/packs.
        - localAppDataRoot optionally overrides user-global discovery from <root>/WpfDevTools/Composer/Packs.

        EXAMPLES:
        """ + CanonicalExamples;
}
