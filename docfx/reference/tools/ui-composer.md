# UI Composer Tools

UI Composer tools work with local Composer extension packs and blueprint inputs. They do not inspect a running WPF target and should be used before catalog, validation, rendering, preview compile, or apply workflows.

## `list_ui_block_packs`

Lists installed UI block packs from built-in, project-local, and user-global roots. The response includes pack id, version, scope, block count, recipe count, example count, renderer count, source repository, readiness metadata, diagnostics, and available block kinds.

Request options:

- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The tool omits absolute pack root paths from its public payload. Use `structuredContent` as the canonical result and treat `content[0].text` as a compact fallback.

## `get_ui_block_catalog`

Returns block catalog entries from enabled Composer packs. Use it after `list_ui_block_packs` when an agent needs concrete block kinds, properties, slot names, `allowedKinds`, renderer availability, or source hint summaries before creating a blueprint.

Request options:

- `packIds`: optional pack id filter, such as `["wpfui"]`.
- `category`: optional block category filter.
- `kindPrefix`: optional pack-qualified kind prefix.
- `composableOnly`: when true, returns only blocks with an available renderer template.
- `kind`: optional exact pack-qualified block kind for single-block detail.
- `includeRecipes`: when true, also returns recipe catalog entries for use with `expand_ui_recipe`.

Catalog entries include source hint paths only. They do not copy third-party source code into tool output.

## `validate_ui_blueprint`

Validates UI blueprint JSON against the installed Composer pack contracts. Use it after `list_ui_block_packs` and `get_ui_block_catalog`, before rendering XAML or applying generated UI.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response keeps `success=true` for a completed validation call and reports blueprint validity in `valid`. Validation issues include `jsonPath`, `code`, `message`, `repairSuggestion`, and relevant `allowedKinds` or `allowedValues`.

## `expand_ui_recipe`

Expands a starter recipe into a full UI blueprint and runs blueprint validation immediately. Use `get_ui_block_catalog` with `includeRecipes=true` to discover recipe ids and inputs before calling this tool.

Request options:

- `recipeId`: required pack-qualified recipe id, such as `wpfui.shellWithNavigation`.
- `inputs`: optional JSON object with recipe input values. Omitted inputs use recipe defaults when available.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response includes `valid`, `recipeId`, the expanded `blueprint`, and the nested validation result. Built-in WPF UI starter recipes cover navigation shell, dashboard card, data grid page, dialog flow, and tabbed settings patterns.

## `render_ui_blueprint`

Runs a dry-run XAML render for a valid UI blueprint. Use it after `validate_ui_blueprint` or `expand_ui_recipe` to inspect generated XAML, required package references, and application resource setup before any file-writing apply workflow.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `targetPath`: optional target XAML path suggestion. The renderer reports it in the file plan but does not write it.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response keeps `success=true` for a completed render call and reports render validity in `valid`. Successful results include `xaml`, `requiredNuGetPackages`, `requiredResources`, and a `filePlan` with `wouldWriteFiles=false`. Invalid results return validation or render issues with `jsonPath`, `code`, `message`, and `repairSuggestion`.

## `preview_ui_blueprint`

Compiles generated UI Composer XAML in a temporary WPF preview project. Use it after `render_ui_blueprint` when an agent needs CI-friendly compile-smoke evidence before applying generated UI to a real project.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `restoreEnabled`: optional boolean that defaults to true. When false, the temporary project is built with `--no-restore` so missing-restore diagnostics can be validated deterministically.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The tool writes only to an isolated temporary preview directory and deletes it after the compile smoke. The preview project uses local WPF UI stubs, so tests do not depend on a NuGet cache or network access. Successful results include `buildSucceeded=true`, generated `xaml`, captured `buildOutput`, and a `previewHost` summary. Build failures return diagnostics that map back to the blueprint root and renderer template path when available.

## `apply_ui_blueprint`

Produces a guarded apply plan for a UI blueprint. The default is dry-run, so agents can inspect the generated view file path, required resources, package plan, and binding contract stub before any write is allowed.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `projectRoot`: required local WPF project root used for path planning and write allowlist checks.
- `targetPath`: optional target XAML file path. It must stay inside `projectRoot`.
- `dryRun`: optional boolean that defaults to true.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

Non-dry-run writes require `WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true` and an exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match. The tool rejects paths outside `projectRoot`, creates a backup when updating an existing view, includes a `WPFDEVTOOLS_BLUEPRINT_SOURCE` header, preserves `WPFDEVTOOLS_SAFE_SLOT` manual-edit markers, and does not run NuGet restore.
