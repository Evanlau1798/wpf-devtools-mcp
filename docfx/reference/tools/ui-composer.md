# UI Composer Tools

UI Composer tools work with local Composer extension packs and blueprint inputs. They do not inspect a running WPF target and should be used before catalog, validation, rendering, preview compile, or apply workflows.

## Contract Compatibility

Composer currently supports these v1 contracts and fails closed when `schemaVersion` is missing or different:

| Contract | Supported version | Compatibility policy |
|---|---|---|
| UI pack | `wpfdevtools.ui-pack.v1` | Reads the artifact as-is; install/import workflows copy pack files instead of rewriting them. |
| UI block | `wpfdevtools.ui-block.v1` | Resolves pack-qualified block kinds from enabled packs only. |
| UI recipe | `wpfdevtools.ui-recipe.v1` | Expands recipes only after the declared required packs are available. |
| UI blueprint | `wpfdevtools.ui-blueprint.v1` | Requires `packs[]`, `primaryPack`, and pack-qualified block kinds; optional `resourceVariants` selects pack-owned resource variants. |
| Source lock | `wpfdevtools.source-lock.v1` | Preserves provenance metadata for loaded packs. |
| Pack install manifest | `wpfdevtools.pack-install-manifest.v1` | Records copied pack installation metadata without changing the pack artifact. |
| Composer project | `wpfdevtools.composer-project.v1` | Reserved for project-local Composer configuration. |

This contract is still pre-release. Beta builds may intentionally correct v1 shapes without legacy aliases; packs, validators, docs, and the extension-pack creator must move together. Stable-release compatibility policy starts with the first public stable contract.

## Data-driven visual foundation

- Native WPF layout comes from the explicit `core@0.1.0` pack with `role="layout-pack"`. Use qualified kinds such as `core.stack`, `core.grid`, `core.rowDefinition`, `core.columnDefinition`, `core.gridCell`, `core.border`, `core.text`, and `core.template`.
- `core.grid` supports real rows, columns, grid cells, spanning, alignment, and WPF `gridLength` values. Layout behavior is pack data, not an engine primitive or WPF UI special case.
- The built-in WPF UI visual set includes `wpfui.numberBox`, `wpfui.toggleSwitch`, and `wpfui.progressRing`, plus configurable typography, margin, padding, alignment, width, and window content constraints.
- Property contracts can expose `minimum`, `maximum`, `integer`, `thickness`, and `gridLength` constraints. Slot `allowedKinds` accepts exact qualified kinds, `*`, or `<pack-id>.*`; `xamlItemTemplate` applies a declared wrapper to each child.
- Packs may expose named `resourceVariants` with a default and a pack-owned `appearance` (`light`, `dark`, or `neutral`). A blueprint selects variants by pack id, so Composer never needs library-specific theme logic. A block property may declare `visualRole` as `surface`; validation then emits `SurfaceThemeContrastRisk` at the exact property path when an explicit surface conflicts with a selected theme-styled subtree.
- Third-party renderers that emit a pack XML namespace declare safe structural preview metadata in `pack.json`. Composer generates preview types from that metadata and returns `PreviewContractMissing` when a used custom namespace has no contract. Native-only third-party renderers need no stub contract. Packs cannot provide arbitrary preview C#.
- Preview metadata remains pack-neutral: use `tabControl` or `tabItem` for semantic subclasses that receive native base-targeted styles. Do not redeclare members inherited from any selected `baseKind`; use native properties such as `Window.Content`, sizing, commands, items, and tab state directly in renderer XAML. Composer rejects shadow declarations that would disconnect authored values from the native visual tree, commands, styles, or templates. Renderer attributes whose entire value is an unset property token are omitted; explicit empty strings and literal empty attributes are preserved. Leave inheritable visual properties such as `Foreground` unset unless the blueprint explicitly overrides the active theme.
- Any blueprint node may declare a stable `elementName` and `automationId`. Composer validates safe syntax and tree-wide uniqueness, then persists them as WPF `x:Name` and `AutomationProperties.AutomationId` on the renderer root. Duplicate values fail with `DuplicateElementName` or `DuplicateAutomationId`; a conflicting pack-owned root identity fails instead of silently rewriting a renderer contract.

For an original app, query `get_ui_block_catalog` with `includeRecipes=false` first and choose a creative brief from available capabilities. Request recipes later as optional accelerators; do not let the first recipe determine the app concept.

## Composer observability

Composer tool responses include an `observability` object with local structured logs, per-call metrics, top diagnostic codes, and a privacy policy summary. This data is returned only in the MCP response or pack import plan; it is not exported to a remote service by default. Set `WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true` to make that disabled policy explicit in hosted environments.

The observability payload does not include blueprint JSON, generated XAML, full user file content, secrets, or absolute local paths. Logs keep stable diagnostic codes plus short remediation text so agents can debug validation, render dry-run, apply, security rejection, rollback, preview compile, and pack import paths without copying project content.

## `list_ui_block_packs`

Lists installed UI block packs from built-in, project-local, and user-global roots. Each entry includes `kind`, `themeTokens`, `resourceVariants`, `role`, `required`, counts, provenance, readiness metadata, and available block kinds. `resourceVariants.defaultVariant` and its ordered variant ids/appearances are the authoritative pack-neutral resource choices. `role` is the pack-kind-derived suggested blueprint role, while `required=true` marks a default required declaration; `required=false` never permits omitting a pack whose blocks the blueprint uses. Use the top-level `allowedPackRoles` as the authoritative pack-neutral values for blueprint `packs[].role`; do not guess a role from a pack id.

Request options:

- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The tool omits absolute pack root paths from its public payload. Use `structuredContent` as the canonical result and treat `content[0].text` as a compact fallback.

## `import_ui_block_pack`

Validates a normalized extension-pack ZIP and, after explicit approval, installs it only under `<projectRoot>/.wpfdevtools/packs`. The default dry-run returns the pack identity, archive SHA256, destination root, and relative file plan without writing.

Request options:

- `archivePath`: required absolute local path to the reviewed normalized pack ZIP.
- `projectRoot`: required absolute local WPF project root; this is the only write boundary.
- `dryRun`: defaults to `true`. Review the archive hash and complete file plan first.
- `confirmImport`: must be `true` when `dryRun=false`.
- `allowOverwrite`: defaults to `false`; enable only for a reviewed replacement of the same pack id and version.

Non-dry-run imports also require `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`, `WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`, and an exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match. The importer rejects unsafe archive entries, invalid pack contracts, destination reparse points, and writes outside the project-local registry. It never edits project files, package references, resources, XAML, code-behind, or ViewModels.

## `get_ui_block_catalog`

Returns block catalog entries from enabled Composer packs. Use it after `list_ui_block_packs` when an agent needs concrete block kinds, properties, slot names, `allowedKinds`, renderer availability, or source hint summaries before creating a blueprint.

Request options:

- `packIds`: optional pack id filter, such as `["sample"]`.
- `category`: optional block category filter.
- `kindPrefix`: optional pack-qualified kind prefix.
- `composableOnly`: when true, returns only blocks with an available renderer template.
- `kind`: optional exact pack-qualified block kind for single-block detail.
- `includeRecipes`: when true, also returns recipe catalog entries for use with `expand_ui_recipe`.

Catalog entries include source hint paths only. They do not copy third-party source code into tool output.

Pack authors can provide inert `description` text for blocks, properties, and slots. Properties can also provide `previewWarning` when structural preview may differ from final package measurement or styling. Read these pack-defined fields before choosing values; they describe renderer behavior without adding library-specific logic to Composer.

The response also includes `authoringGuidance`. Its `strategy="brief-first"` and `creativeBriefRequired=true` fields tell an Agent to establish an independent product purpose and information architecture from discovered capabilities. `includeRecipes` defaults to false; request recipes only later as optional accelerators or fragments.

Every catalog item includes a pack-neutral `compositionSkeleton` generated from
that block's own contract. It contains the exact `kind`, values for required
properties, and empty arrays for declared slots. Copy the compact node into a
blueprint and add children or optional properties without retyping pack-specific
kind and slot names.

## Optional blueprint draft transport

Use `create_ui_blueprint_draft` when a multi-step workflow should avoid retransmitting and double-serializing the same document. It accepts one blueprint JSON object and returns an opaque `draftRef` without echoing the document. The immutable, process-local store is bounded to 32 drafts, 65,536 characters per draft, and 30 minutes per entry. References are unguessable, are never persisted, and become invalid when their MCP server process exits, their lifetime expires, or capacity eviction occurs.

Use `patch_ui_blueprint_draft` with a live reference to create a new immutable derived reference. For broad object changes, pass a JSON Merge Patch object: null removes an object property, nested objects merge recursively, and arrays or scalar values replace their target. For one nested edit, pass an exact `jsonPath` plus a native JSON `value`, or set `remove=true` without a value. The source reference never changes. Every successful derivation returns a bounded `changeSummary` with changed paths and compact before/after values instead of echoing the full blueprint. A missing, expired, or evicted reference returns `BlueprintDraftNotFound` with recovery guidance.

The seven downstream tools that take `blueprintJson` also accept an opaque `draftRef`: `compose_ui_blueprint`, `validate_ui_blueprint`, `render_ui_blueprint`, `preview_ui_blueprint`, `repair_ui_blueprint`, `apply_ui_blueprint`, and `apply_ui_project_integration`. Direct `blueprintJson` remains the simplest option for one-shot workflows.

## `create_ui_blueprint_draft`

Creates one bounded ephemeral draft. The response includes `draftRef`, `characterCount`, `expiresAt`, `immutable=true`, and exact retention metadata; it intentionally omits the stored JSON.

## `patch_ui_blueprint_draft`

Derives a new draft in one of two mutually exclusive modes:

- Broad change: pass `draftRef` and `patchJson` for JSON Merge Patch.
- Surgical change: pass `draftRef`, an exact path such as `$.layout.slots.children[0].properties.text`, and `value`; use bracket-quoted segments such as `$.layout.properties["accent.color"]` when a pack-defined property key is not a simple identifier. To delete the target, omit `value` and set `remove=true`.

The response returns the new reference, `sourceDraftRef`, retention metadata, and a compact `changeSummary` containing `changeCount`, bounded `changes`, and truncation metadata. Each change identifies `jsonPath`, `changeType`, and compact `before`/`after` values. It never echoes the full blueprint. Use `compose_ui_blueprint` instead when inserting a catalog block into a slot array.

## `compose_ui_blueprint`

Inserts one pack-defined `compositionSkeleton` into an existing blueprint slot and validates the resulting document. Use it to build nested interfaces incrementally without manually rewriting deep JSON. This operation is pack-neutral and never writes files.

Request options:

- `blueprintJson`: current full blueprint JSON text or an opaque `draftRef`.
- `targetPath`: exact slot path. Use `$.layout.slots.<slot>` for a root slot, or include an explicit child index before each nested slot, such as `$.layout.slots.content[0].slots.actions`.
- `kind`: exact pack-qualified block kind from `get_ui_block_catalog` with `composableOnly=true`.
- `properties`: optional JSON object of pack-defined values to apply during insertion. The installed block contract validates property names, types, ranges, and allowed values.
- `insertionIndex`: optional zero-based position; omit it to append.
- `projectRoot` and `localAppDataRoot`: optional pack discovery roots.

Use `properties` when the block should be configured at insertion time; this avoids a follow-up edit through a long nested path while keeping the pack's `compositionSkeleton` authoritative. With raw JSON input, `composed=true` returns a new `blueprint`, compact `blueprintJson`, exact `insertedPath`, and validation result. With draft input, it returns a new immutable `draftRef` and omits the full document; the source draft remains unchanged. An invalid draft-derived candidate is retained under `candidateDraftRef`, while raw input retains the existing `invalidCandidate` and `candidateBlueprintJson` recovery shape. Neither path writes project files. Ambiguous paths and non-composable blocks return actionable errors without a candidate.

## `validate_ui_blueprint`

Validates UI blueprint JSON against the installed Composer pack contracts. Use it after `list_ui_block_packs` and `get_ui_block_catalog`, before rendering XAML or applying generated UI.

Request options:

- `blueprintJson`: required raw UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`, or an opaque `draftRef`.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response keeps `success=true` for a completed validation call and reports blueprint validity in `valid`. Validation issues include `jsonPath`, `code`, `message`, `repairSuggestion`, and relevant `allowedKinds` or `allowedValues`. Unknown pack-owned resource selections fail with `UnknownResourceVariant`; explicit surface/theme conflicts return the bounded `SurfaceThemeContrastRisk` warning before preview or apply. Node-level `elementName` and `automationId` values are checked for safe syntax and uniqueness before rendering. `blueprintSize` reports `currentCharacters`, `maximumCharacters`, `remainingCharacters`, and `utilizationPercent` so an agent can simplify the document before it reaches the public input limit.

## `expand_ui_recipe`

Expands a starter recipe into a full UI blueprint and runs blueprint validation immediately. Use `get_ui_block_catalog` with `includeRecipes=true` to discover recipe ids and inputs before calling this tool.

Request options:

- `recipeId`: required pack-qualified recipe id, such as `sample.workspaceStarter`.
- `inputs`: optional JSON object with recipe input values. Omitted inputs use recipe defaults when available.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response includes `valid`, `recipeId`, the expanded `blueprint`, and the nested validation result. Built-in WPF UI starter recipes cover navigation shell, dashboard card, data grid page, and tabbed settings patterns.

The built-in catalog intentionally excludes host-backed controls such as WPF UI `Snackbar` and `ContentDialog`. Those controls require presenter, host, or runtime show behavior that a standalone layout node cannot represent safely. Runtime catalog discovery is authoritative; third-party packs may expose comparable controls only when their renderer and behavior contracts cover those requirements.

For a one-shot raw workflow, serialize the `blueprint` object from `structuredContent` to JSON text before the next Composer call, then pass it under the `blueprintJson` parameter name. For repeated calls, create a draft once and pass its `draftRef` through that same `blueprintJson` parameter. Do not pass the object under a parameter named `blueprint`.

Every Composer `blueprintJson` parameter accepts at most 65,536 characters. Use a compact serializer so formatting whitespace does not consume that limit. In PowerShell, serialize the structured blueprint object with `$blueprint | ConvertTo-Json -Depth 100 -Compress`; the explicit depth preserves nested properties in built-in recipes. Other clients should disable indentation when serializing the expanded object.

## `render_ui_blueprint`

Runs a dry-run XAML render for a valid UI blueprint. Use it after `validate_ui_blueprint` or `expand_ui_recipe` to inspect generated XAML, required package references, and application resource setup before any file-writing apply workflow.

Request options:

- `blueprintJson`: required raw UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`, or an opaque `draftRef`.
- `targetPath`: optional target XAML path suggestion. The renderer reports it in the file plan but does not write it.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response keeps `success=true` for a completed render call and reports render validity in `valid`. Successful results include `xaml`, `requiredNuGetPackages`, `requiredResources`, `packageIntegrationGuidance`, and a `filePlan` with `wouldWriteFiles=false`. Package guidance is derived from the target project and never edits project or central package files. Invalid results return validation or render issues with `jsonPath`, `code`, `message`, and `repairSuggestion`.

## `preview_ui_blueprint`

Compiles generated UI Composer XAML in a temporary WPF preview project. Use it after `render_ui_blueprint` when an agent needs CI-friendly compile, host-load, or runtime scene/layout evidence before applying generated UI to a real project.

Request options:

- `blueprintJson`: required raw UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`, or an opaque `draftRef`.
- `restoreEnabled`: optional boolean that defaults to true. When false, the temporary project is built with `--no-restore` so missing-restore diagnostics can be validated deterministically.
- `startHost`: optional boolean that defaults to false. When true, the temporary preview host starts after a successful build and reports generated-view load status.
- `includeRuntimeDiagnostics`: optional boolean that defaults to false. When true with `startHost=true`, the tool reuses `connect`, `get_ui_summary(depthMode="semantic")`, a bounded `find_elements` lookup plan for generated and renderer-provided correlation names, and `get_layout_info` against the temporary host. This requires `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`.
- `includeScreenshotDiagnostics`: optional boolean that defaults to false. When true with `startHost=true`, the tool enables runtime diagnostics and requests a screenshot only if both `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` and `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true`.
- `screenshotOutputMode`: optional closed value that defaults to `metadata`; use `file` to retain a server-owned PNG and return its `resourceUri` plus an exact `resourceRead` request. Call `resourceRead.method` (`resources/read`) with `resourceRead.params` in the same MCP server session, after the temporary preview host exits but before that server session ends. Other values, including `base64`, return `InvalidArgument` before preview work starts.
- `screenshotMaxWidth` and `screenshotMaxHeight`: optional positive bounds that default to 1024. Keep these defaults for reliable visual consumption across constrained Agent image bridges; pass explicit null values only when full rendered dimensions are required for archival evidence.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The tool writes only to an isolated temporary preview directory and deletes it after the compile smoke. The preview project generates structural stubs from each pack's safe metadata, so the engine does not hardcode WPF UI or execute pack code. Every completed preview result reports `visualFidelity="structural-stub"` and provides `visualValidationGuidance`: preview screenshots are structural-only evidence and must not be used to approve final styling. Its `visualComparisonChecklist` gives a compact preview-versus-final-app comparison for window chrome, icons, control templates, and layout/spacing, plus the required final-app check for each area. Apply the blueprint, build and launch the real WPF application, then inspect that application for final visual validation. Successful results include `buildSucceeded=true`, generated `xaml`, captured `buildOutput`, and a `previewHost` summary. Runtime diagnostics are opt-in and returned under `previewHost.runtimeDiagnostics`. A successful `screenshotOutputMode="file"` resource is detached from the short-lived preview process, remains subject to the 24-hour and 100-resource `SessionManager` bounds, and is removed on expiry, eviction, or server session-manager disposal. If a client displays missing or dark regions while semantic evidence is complete, follow `screenshotVerificationGuidance`: re-read or reopen the same screenshot resource and verify its SHA-256 before spending another preview cycle or reporting a product defect. If the same decoded bytes remain sparse, retry `preview_ui_blueprint` with `screenshotMaxWidth=1024` and `screenshotMaxHeight=1024`; the temporary host has already exited when the original call returns. Do not report a product visual failure until the verified bounded image and semantic summary agree. Build failures return diagnostics that map back to the blueprint root and renderer template path when available.

The `propertyWarnings` array contains only pack-defined warnings for properties explicitly present in the submitted blueprint. Each entry reports the exact `jsonPath`, `blockKind`, `propertyName`, and `message`, so an Agent can focus final-app validation on the affected layout or styling decision instead of treating every structural-preview limitation as equally relevant.

The `elementCorrelations` array maps each renderer root's transient or safely preserved `x:Name` (`elementName`) to an exact blueprint `jsonPath` and `blockKind`. Generated names avoid every name reserved by the active renderer templates. Runtime diagnostics query the generated prefix once and query distinct renderer-provided names exactly, with a fixed upper bound. Match those results with `get_ui_summary` to connect preview evidence to the authored node. Correlation metadata is never stored in the blueprint or emitted by normal render/apply; existing renderer names remain unchanged so `ElementName` bindings keep working.

## `repair_ui_blueprint`

Turns validation, render, compile, or preview diagnostics into blueprint-first repair actions. Use it after `validate_ui_blueprint`, `render_ui_blueprint`, or `preview_ui_blueprint` returns issues.

Request options:

- `blueprintJson`: required raw UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`, or an opaque `draftRef`.
- `diagnosticsJson`: optional diagnostics JSON object or array from render or preview results.
- `targetPath`: optional target XAML path suggestion for render diagnostics only. The tool does not write it.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response includes `repairable`, `generatedXamlPatch=false`, `actionCount`, and `actions`. Only content-equivalent duplicate guidance becomes one action; matching `issueCode` and `jsonPath` alone does not merge different messages, suggestions, values, or renderer paths. For merged equivalents, `source` preserves the first observation and ordered `sources[]` lists every contributing validation, renderer, or diagnostic source. Actions identify whether the repair belongs in the blueprint or in the pack renderer template contract. The tool never patches generated XAML directly.

## `apply_ui_blueprint`

Produces a guarded apply plan for a UI blueprint. The default is dry-run, so agents can inspect the generated view file path, required resources, package plan, binding contract stub, and deterministic `projectIntegrationPlan` before any write is allowed.

Request options:

- `blueprintJson`: required raw UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`, or an opaque `draftRef`.
- `projectRoot`: required local WPF project root used for path planning and write allowlist checks.
- `targetPath`: optional target XAML file path. It must stay inside `projectRoot`.
- `dryRun`: optional boolean that defaults to true.
- `confirmApply`: optional boolean that must be true for non-dry-run writes after the dry-run plan has been reviewed.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

Non-dry-run writes require `confirmApply=true`, `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`, `WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`, and an exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match. A successful confirmed response preserves the executed file plan from the pre-write target state: a newly written file remains `action="create"`, while an existing file remains `action="update"` and reports its backup path. The tool rejects paths outside `projectRoot`, creates a backup when updating an existing view, includes a `WPFDEVTOOLS_BLUEPRINT_SOURCE` header, preserves `WPFDEVTOOLS_SAFE_SLOT` manual-edit markers, and does not run NuGet restore.

The dry-run `projectIntegrationPlan` is pack-neutral. Its operations name exact target paths, semantic purposes, current-file preconditions, and proposed SHA-256 values for package references, application resources, startup selection, and pack-declared code-behind base types. It does not expose full existing project-file content through an ungated dry-run. The plan hash binds the reviewed semantic operations to the exact proposed content and current file state.

## `apply_ui_project_integration`

Applies only the `projectIntegrationPlan` returned by the latest `apply_ui_blueprint` dry-run. Pass the same raw `blueprintJson` or opaque `draftRef`, `projectRoot`, `targetPath`, and pack discovery scope together with `reviewedPlanHash` and `confirmIntegration=true`.

This destructive tool requires `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`, `WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`, and an exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match. It regenerates the plan immediately before writing. Any pack, blueprint, target, or project-file change produces `IntegrationPlanChanged` and no write occurs.

Only plan-generated package-reference, central-package-version, application-XAML, and code-behind-base-type operations are permitted. Package IDs, XAML namespaces, resources, and base types come from the selected packs; the engine contains no control-library branch. Existing files use atomic replacement and each returned change records `backupPath` plus `rollbackAction`. If a later operation fails, earlier changes are rolled back and the response reports whether rollback completed.

## Apply-to-build workflow

`apply_ui_blueprint` writes reviewed view XAML; it does not silently edit the project file, application resources, code-behind, ViewModel, or startup flow. Use the returned plans as the authoritative integration checklist:

1. Run dry apply and review `filePlan`, `requiredNuGetPackages`, `packageIntegrationGuidance`, `resourcePlan`, `viewModelBindingContract`, `behaviorIntegrationContract`, and `projectIntegrationPlan`.
2. Run confirmed apply only after the project-root gates are scoped to the intended project.
3. When `projectIntegrationPlan.ready=true`, call `apply_ui_project_integration` with its exact `reviewedPlanHash` only after reviewing every operation. A stale hash fails with `IntegrationPlanChanged`; successful changes include `backupPath` and rollback evidence.
4. When the machine-applicable plan is not ready, follow `packageIntegrationGuidance` manually for every pack-declared package. Detection is static XML best-effort; every result reports `inspectionConfidence`, `inspectionReason`, `inspectedFiles`, and `inspectionLimitations`, including the lack of evaluated MSBuild imports and conditions. When `mode="project"`, add each returned `projectPackageReference` to the reported project file. When `mode="central"` because `ManagePackageVersionsCentrally=true`, add the versionless `projectPackageReference` to the project and the matching `centralPackageVersion` to `Directory.Packages.props`. When `mode="unknown"`, package snippets are null: inspect the project first and do not infer either integration shape.
5. Apply each entry in `resourcePlan` manually only when it is not covered by a ready reviewed integration plan. The plan already reflects the blueprint's `resourceVariants` selections or each pack's default. Treat the returned pack data as authoritative; do not assume a specific library namespace or dictionary.
6. If `filePlan` contains `role="code-behind-integration"` and the reviewed integration plan is not ready, use its action and the pack renderer's validated `codeBehindBaseType` so generated XAML `x:Class` and code-behind inherit the same type.

7. Treat `behaviorIntegrationContract.status="required"` as a release gate. Each interaction includes `bindingStatus`, raw `commandBinding`, and a nullable parsed `commandPath`. Complex valid WPF bindings remain required when their path is unresolved; resolve them in the final view. Navigation commands receive `commandParameter` and must update selected application state and destination content; action commands must perform observable application behavior and expose an appropriate `CanExecute` policy. These are application contracts, not generated business logic.
8. Restore, build, and launch the actual application separately:

   ```powershell
   dotnet restore .\YourApp.csproj
   dotnet build .\YourApp.csproj --no-restore
   dotnet run --project .\YourApp.csproj --no-build
   ```

9. Validate the running app, not only the structural preview. Use `connect`, `get_ui_summary`, focused element reads, and `element_screenshot(outputMode="file")`. Invoke every interaction from `behaviorIntegrationContract` and verify a state or visible content change. For any diagnostic mutation, use `capture_state_snapshot`, `get_state_diff`, and `restore_state_snapshot`.

Do not approve a generated application merely because it compiles or because a button reports click-ready. A command-bound control remains incomplete until its DataContext command and observable result have been implemented and verified in the launched application.
