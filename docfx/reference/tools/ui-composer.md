# UI Composer Tools

UI Composer tools work with local Composer extension packs and blueprint inputs. They do not inspect a running WPF target and should be used before catalog, validation, rendering, preview compile, or apply workflows.

## Contract Compatibility

Composer currently supports these v1 contracts and fails closed when `schemaVersion` is missing or different:

| Contract | Supported version | Compatibility policy |
|---|---|---|
| UI pack | `wpfdevtools.ui-pack.v1` | Reads the artifact as-is; install/import workflows copy pack files instead of rewriting them. |
| UI block | `wpfdevtools.ui-block.v1` | Resolves pack-qualified block kinds from enabled packs only. |
| UI recipe | `wpfdevtools.ui-recipe.v1` | Expands recipes only after the declared required packs are available. |
| UI blueprint | `wpfdevtools.ui-blueprint.v1` | Requires `packs[]`, `primaryPack`, and pack-qualified block kinds. |
| Source lock | `wpfdevtools.source-lock.v1` | Preserves provenance metadata for loaded packs. |
| Pack install manifest | `wpfdevtools.pack-install-manifest.v1` | Records copied pack installation metadata without changing the pack artifact. |
| Composer project | `wpfdevtools.composer-project.v1` | Reserved for project-local Composer configuration. |

This contract is still pre-release. Beta builds may intentionally correct v1 shapes without legacy aliases; packs, validators, docs, and the extension-pack creator must move together. Stable-release compatibility policy starts with the first public stable contract.

## Data-driven visual foundation

- Native WPF layout comes from the explicit `core@0.1.0` pack with `role="layout-pack"`. Use qualified kinds such as `core.stack`, `core.grid`, `core.rowDefinition`, `core.columnDefinition`, `core.gridCell`, `core.border`, `core.text`, and `core.template`.
- `core.grid` supports real rows, columns, grid cells, spanning, alignment, and WPF `gridLength` values. Layout behavior is pack data, not an engine primitive or WPF UI special case.
- The built-in WPF UI visual set includes `wpfui.numberBox`, `wpfui.toggleSwitch`, and `wpfui.progressRing`, plus configurable typography, margin, padding, alignment, width, and window content constraints.
- Property contracts can expose `minimum`, `maximum`, `integer`, `thickness`, and `gridLength` constraints. Slot `allowedKinds` accepts exact qualified kinds, `*`, or `<pack-id>.*`; `xamlItemTemplate` applies a declared wrapper to each child.
- Third-party renderers that emit a pack XML namespace declare safe structural preview metadata in `pack.json`. Composer generates preview types from that metadata and returns `PreviewContractMissing` when a used custom namespace has no contract. Native-only third-party renderers need no stub contract. Packs cannot provide arbitrary preview C#.
- Preview metadata remains pack-neutral: use `tabControl` or `tabItem` for semantic subclasses that receive native base-targeted styles. Native inherited properties such as `SelectedIndex` and `Header` must not be redeclared; Composer rejects shadow declarations that would disconnect base styles or templates from rendered values. Renderer attributes whose entire value is an unset property token are omitted; explicit empty strings and literal empty attributes are preserved. Leave inheritable visual properties such as `Foreground` unset unless the blueprint explicitly overrides the active theme.

For an original app, query `get_ui_block_catalog` with `includeRecipes=false` first and choose a creative brief from available capabilities. Request recipes later as optional accelerators; do not let the first recipe determine the app concept.

## Composer observability

Composer tool responses include an `observability` object with local structured logs, per-call metrics, top diagnostic codes, and a privacy policy summary. This data is returned only in the MCP response or pack import plan; it is not exported to a remote service by default. Set `WPFDEVTOOLS_COMPOSER_TELEMETRY_DISABLED=true` to make that disabled policy explicit in hosted environments.

The observability payload does not include blueprint JSON, generated XAML, full user file content, secrets, or absolute local paths. Logs keep stable diagnostic codes plus short remediation text so agents can debug validation, render dry-run, apply, security rejection, rollback, preview compile, and pack import paths without copying project content.

## `list_ui_block_packs`

Lists installed UI block packs from built-in, project-local, and user-global roots. The response includes pack id, version, scope, block count, recipe count, example count, renderer count, source repository, readiness metadata, diagnostics, and available block kinds. Use the top-level `allowedPackRoles` as the authoritative pack-neutral values for blueprint `packs[].role`; do not guess a role from a pack id.

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

Pack authors can provide inert `description` text for blocks, properties, and slots. Properties can also provide `previewWarning` when structural preview may differ from final package measurement or styling. Read these pack-defined fields before choosing values; they describe renderer behavior without adding library-specific logic to Composer.

Every catalog item includes a pack-neutral `compositionSkeleton` generated from
that block's own contract. It contains the exact `kind`, values for required
properties, and empty arrays for declared slots. Copy the compact node into a
blueprint and add children or optional properties without retyping pack-specific
kind and slot names.

## `compose_ui_blueprint`

Inserts one pack-defined `compositionSkeleton` into an existing blueprint slot and validates the resulting document. Use it to build nested interfaces incrementally without manually rewriting deep JSON. This operation is pack-neutral and never writes files.

Request options:

- `blueprintJson`: current full blueprint JSON text.
- `targetPath`: exact slot path. Use `$.layout.slots.<slot>` for a root slot, or include an explicit child index before each nested slot, such as `$.layout.slots.content[0].slots.actions`.
- `kind`: exact pack-qualified block kind from `get_ui_block_catalog` with `composableOnly=true`.
- `insertionIndex`: optional zero-based position; omit it to append.
- `projectRoot` and `localAppDataRoot`: optional pack discovery roots.

When `composed=true`, the response returns a new `blueprint`, compact `blueprintJson`, exact `insertedPath`, and validation result. When the path is ambiguous, the block is not composable, or pack validation rejects the child, `composed=false` omits the candidate blueprint and returns actionable errors.

## `validate_ui_blueprint`

Validates UI blueprint JSON against the installed Composer pack contracts. Use it after `list_ui_block_packs` and `get_ui_block_catalog`, before rendering XAML or applying generated UI.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response keeps `success=true` for a completed validation call and reports blueprint validity in `valid`. Validation issues include `jsonPath`, `code`, `message`, `repairSuggestion`, and relevant `allowedKinds` or `allowedValues`. `blueprintSize` reports `currentCharacters`, `maximumCharacters`, `remainingCharacters`, and `utilizationPercent` so an agent can simplify the document before it reaches the public input limit.

## `expand_ui_recipe`

Expands a starter recipe into a full UI blueprint and runs blueprint validation immediately. Use `get_ui_block_catalog` with `includeRecipes=true` to discover recipe ids and inputs before calling this tool.

Request options:

- `recipeId`: required pack-qualified recipe id, such as `wpfui.shellWithNavigation`.
- `inputs`: optional JSON object with recipe input values. Omitted inputs use recipe defaults when available.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response includes `valid`, `recipeId`, the expanded `blueprint`, and the nested validation result. Built-in WPF UI starter recipes cover navigation shell, dashboard card, data grid page, and tabbed settings patterns.

The built-in catalog intentionally excludes host-backed controls such as WPF UI `Snackbar` and `ContentDialog`. Those controls require presenter, host, or runtime show behavior that a standalone layout node cannot represent safely. Runtime catalog discovery is authoritative; third-party packs may expose comparable controls only when their renderer and behavior contracts cover those requirements.

Serialize the `blueprint` object from `structuredContent` to JSON text before the next Composer call, then pass it under the `blueprintJson` parameter name. Do not pass the object under a parameter named `blueprint`; validation, render, preview, repair, and apply intentionally share the same JSON-string document shape.

Every Composer `blueprintJson` parameter accepts at most 65,536 characters. Use a compact serializer so formatting whitespace does not consume that limit. In PowerShell, serialize the structured blueprint object with `$blueprint | ConvertTo-Json -Depth 100 -Compress`; the explicit depth preserves nested properties in built-in recipes. Other clients should disable indentation when serializing the expanded object.

## `render_ui_blueprint`

Runs a dry-run XAML render for a valid UI blueprint. Use it after `validate_ui_blueprint` or `expand_ui_recipe` to inspect generated XAML, required package references, and application resource setup before any file-writing apply workflow.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `targetPath`: optional target XAML path suggestion. The renderer reports it in the file plan but does not write it.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response keeps `success=true` for a completed render call and reports render validity in `valid`. Successful results include `xaml`, `requiredNuGetPackages`, `requiredResources`, and a `filePlan` with `wouldWriteFiles=false`. Invalid results return validation or render issues with `jsonPath`, `code`, `message`, and `repairSuggestion`.

## `preview_ui_blueprint`

Compiles generated UI Composer XAML in a temporary WPF preview project. Use it after `render_ui_blueprint` when an agent needs CI-friendly compile, host-load, or runtime scene/layout evidence before applying generated UI to a real project.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `restoreEnabled`: optional boolean that defaults to true. When false, the temporary project is built with `--no-restore` so missing-restore diagnostics can be validated deterministically.
- `startHost`: optional boolean that defaults to false. When true, the temporary preview host starts after a successful build and reports generated-view load status.
- `includeRuntimeDiagnostics`: optional boolean that defaults to false. When true with `startHost=true`, the tool reuses `connect`, `get_ui_summary(depthMode="semantic")`, and `get_layout_info` against the temporary host. This requires `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`.
- `includeScreenshotDiagnostics`: optional boolean that defaults to false. When true with `startHost=true`, the tool enables runtime diagnostics and requests a screenshot only if both `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` and `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true`.
- `screenshotOutputMode`: optional closed value that defaults to `metadata`; use `file` to retain a server-owned PNG and return its `resourceUri` plus an exact `resourceRead` request. Call `resourceRead.method` (`resources/read`) with `resourceRead.params` in the same MCP server session, after the temporary preview host exits but before that server session ends. Other values, including `base64`, return `InvalidArgument` before preview work starts.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The tool writes only to an isolated temporary preview directory and deletes it after the compile smoke. The preview project generates structural stubs from each pack's safe metadata, so the engine does not hardcode WPF UI or execute pack code. Every completed preview result reports `visualFidelity="structural-stub"` and provides `visualValidationGuidance`: preview screenshots are structural-only evidence and must not be used to approve final styling. Its `visualComparisonChecklist` gives a compact preview-versus-final-app comparison for window chrome, icons, control templates, and layout/spacing, plus the required final-app check for each area. Apply the blueprint, build and launch the real WPF application, then inspect that application for final visual validation. Successful results include `buildSucceeded=true`, generated `xaml`, captured `buildOutput`, and a `previewHost` summary. Runtime diagnostics are opt-in and returned under `previewHost.runtimeDiagnostics`. A successful `screenshotOutputMode="file"` resource is detached from the short-lived preview process, remains subject to the 24-hour and 100-resource `SessionManager` bounds, and is removed on expiry, eviction, or server session-manager disposal. Build failures return diagnostics that map back to the blueprint root and renderer template path when available.

The `propertyWarnings` array contains only pack-defined warnings for properties explicitly present in the submitted blueprint. Each entry reports the exact `jsonPath`, `blockKind`, `propertyName`, and `message`, so an Agent can focus final-app validation on the affected layout or styling decision instead of treating every structural-preview limitation as equally relevant.

## `repair_ui_blueprint`

Turns validation, render, compile, or preview diagnostics into blueprint-first repair actions. Use it after `validate_ui_blueprint`, `render_ui_blueprint`, or `preview_ui_blueprint` returns issues.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `diagnosticsJson`: optional diagnostics JSON object or array from render or preview results.
- `targetPath`: optional target XAML path suggestion for render diagnostics only. The tool does not write it.
- `projectRoot`: optional WPF project root. When present, project-local packs are discovered from `<projectRoot>/.wpfdevtools/packs`.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

The response includes `repairable`, `generatedXamlPatch=false`, `actionCount`, and `actions`. Only content-equivalent duplicate guidance becomes one action; matching `issueCode` and `jsonPath` alone does not merge different messages, suggestions, values, or renderer paths. For merged equivalents, `source` preserves the first observation and ordered `sources[]` lists every contributing validation, renderer, or diagnostic source. Actions identify whether the repair belongs in the blueprint or in the pack renderer template contract. The tool never patches generated XAML directly.

## `apply_ui_blueprint`

Produces a guarded apply plan for a UI blueprint. The default is dry-run, so agents can inspect the generated view file path, required resources, package plan, and binding contract stub before any write is allowed.

Request options:

- `blueprintJson`: required UI blueprint JSON with `schemaVersion` set to `wpfdevtools.ui-blueprint.v1`.
- `projectRoot`: required local WPF project root used for path planning and write allowlist checks.
- `targetPath`: optional target XAML file path. It must stay inside `projectRoot`.
- `dryRun`: optional boolean that defaults to true.
- `confirmApply`: optional boolean that must be true for non-dry-run writes after the dry-run plan has been reviewed.
- `localAppDataRoot`: optional root for user-global discovery. When omitted, the server uses the current user's LocalApplicationData path if available.

Non-dry-run writes require `confirmApply=true`, `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`, `WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`, and an exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match. A successful confirmed response preserves the executed file plan from the pre-write target state: a newly written file remains `action="create"`, while an existing file remains `action="update"` and reports its backup path. The tool rejects paths outside `projectRoot`, creates a backup when updating an existing view, includes a `WPFDEVTOOLS_BLUEPRINT_SOURCE` header, preserves `WPFDEVTOOLS_SAFE_SLOT` manual-edit markers, and does not run NuGet restore.

## Apply-to-build workflow

`apply_ui_blueprint` writes reviewed view XAML; it does not silently edit the project file, application resources, code-behind, ViewModel, or startup flow. Use the returned plans as the authoritative integration checklist:

1. Run dry apply and review `filePlan`, `requiredNuGetPackages`, `resourcePlan`, `viewModelBindingContract`, and `behaviorIntegrationContract`.
2. Run confirmed apply only after the project-root gates are scoped to the intended project.
3. Add every package in `requiredNuGetPackages`. For a project without central package management:

   ```xml
   <PackageReference Include="WPF-UI" Version="4.3.0" />
   ```

   When `ManagePackageVersionsCentrally=true`, omit `Version` on the `PackageReference` and put it in `Directory.Packages.props`:

   ```xml
   <ItemGroup>
     <PackageVersion Include="WPF-UI" Version="4.3.0" />
   </ItemGroup>
   ```

4. Add the returned WPF UI dictionaries to `App.xaml` and declare the WPF UI namespace:

   ```xml
   <Application xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml" ...>
     <Application.Resources>
       <ResourceDictionary>
         <ResourceDictionary.MergedDictionaries>
           <ui:ThemesDictionary Theme="Dark" />
           <ui:ControlsDictionary />
         </ResourceDictionary.MergedDictionaries>
       </ResourceDictionary>
     </Application.Resources>
   </Application>
   ```

5. If `filePlan` contains `role="code-behind-integration"`, make the generated XAML `x:Class` and its code-behind use the same base type:

   ```csharp
   public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
   {
       public MainWindow() => InitializeComponent();
   }
   ```

6. Treat `behaviorIntegrationContract.status="required"` as a release gate. Implement every listed `commandPath` on the view DataContext. Navigation commands receive `commandParameter` and must update selected application state and destination content; action commands must perform observable application behavior and expose an appropriate `CanExecute` policy. The built-in navigation recipe uses `NavigateCommand` and `PrimaryActionCommand` by default. These are application contracts, not generated business logic.
7. Restore, build, and launch the actual application separately:

   ```powershell
   dotnet restore .\YourApp.csproj
   dotnet build .\YourApp.csproj --no-restore
   dotnet run --project .\YourApp.csproj --no-build
   ```

8. Validate the running app, not only the structural preview. Use `connect`, `get_ui_summary`, focused element reads, and `element_screenshot(outputMode="file")`. Invoke every interaction from `behaviorIntegrationContract` and verify a state or visible content change. For any diagnostic mutation, use `capture_state_snapshot`, `get_state_diff`, and `restore_state_snapshot`.

Do not approve a generated application merely because it compiles or because a button reports click-ready. A command-bound control remains incomplete until its DataContext command and observable result have been implemented and verified in the launched application.
