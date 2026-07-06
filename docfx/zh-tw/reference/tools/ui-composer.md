# UI Composer 工具

UI Composer 工具用於本機 Composer extension pack 與 blueprint 輸入。它們不檢查正在執行的 WPF target，應在 catalog、validation、rendering、preview compile 或 apply workflow 前使用。

## `list_ui_block_packs`

列出 built-in、project-local 與 user-global roots 中已安裝的 UI block packs。回應包含 pack id、version、scope、block count、recipe count、example count、renderer count、source repository、readiness metadata、diagnostics 與可用 block kinds。

Request options:

- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

此 tool 的公開 payload 不會回傳 absolute pack root paths。請以 `structuredContent` 作為 canonical result，`content[0].text` 只作為 compact fallback。

## `get_ui_block_catalog`

從 enabled Composer packs 回傳 block catalog entries。Agent 需要在建立 blueprint 前理解具體 block kinds、properties、slot names、`allowedKinds`、renderer availability 或 source hint summaries 時，先呼叫 `list_ui_block_packs`，再使用此 tool。

Request options:

- `packIds`: optional pack id filter，例如 `["wpfui"]`。
- `category`: optional block category filter。
- `kindPrefix`: optional pack-qualified kind prefix。
- `composableOnly`: true 時只回傳具備 renderer template 的 blocks。
- `kind`: optional exact pack-qualified block kind，用於 single-block detail。
- `includeRecipes`: true 時同時回傳可供 `expand_ui_recipe` 使用的 recipe catalog entries。

Catalog entries 只包含 source hint paths，不會把第三方 source code 複製進 tool output。

## `validate_ui_blueprint`

依照已安裝 Composer pack contracts 驗證 UI blueprint JSON。請在 `list_ui_block_packs` 與 `get_ui_block_catalog` 之後、rendering XAML 或 apply generated UI 之前使用。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

完成 validation 呼叫時 response 會維持 `success=true`，並用 `valid` 表示 blueprint 是否有效。Validation issues 會包含 `jsonPath`、`code`、`message`、`repairSuggestion`，以及相關的 `allowedKinds` 或 `allowedValues`。

## `expand_ui_recipe`

將 starter recipe 展開成完整 UI blueprint，並立即執行 blueprint validation。呼叫前可先使用 `get_ui_block_catalog` 搭配 `includeRecipes=true` 探索 recipe id 與 inputs。

Request options:

- `recipeId`: required pack-qualified recipe id，例如 `wpfui.shellWithNavigation`。
- `inputs`: optional JSON object，提供 recipe input values。省略時會使用 recipe defaults。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

Response 包含 `valid`、`recipeId`、展開後的 `blueprint` 與 nested validation result。Built-in WPF UI starter recipes 覆蓋 navigation shell、dashboard card、data grid page、dialog flow 與 tabbed settings patterns。

## `render_ui_blueprint`

對有效的 UI blueprint 執行 dry-run XAML rendering。請在 `validate_ui_blueprint` 或 `expand_ui_recipe` 後使用，以便在任何寫檔 apply workflow 前檢查 generated XAML、required package references 與 application resource setup。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `targetPath`: optional target XAML path suggestion。Renderer 會在 file plan 回報此路徑，但不會寫入。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

完成 render 呼叫時 response 會維持 `success=true`，並用 `valid` 表示 render 是否有效。成功結果包含 `xaml`、`requiredNuGetPackages`、`requiredResources`，以及 `wouldWriteFiles=false` 的 `filePlan`。無效結果會回傳 validation 或 render issues，包含 `jsonPath`、`code`、`message` 與 `repairSuggestion`。

## `preview_ui_blueprint`

在 temporary WPF preview project 中 compile generated UI Composer XAML。Agent 需要在 apply generated UI 到真實 project 前取得符合 CI 的 compile、host-load，或 runtime scene/layout evidence 時，請在 `render_ui_blueprint` 後使用。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `restoreEnabled`: optional boolean，預設為 true。false 時 temporary project 會用 `--no-restore` build，以便 deterministic 驗證 missing-restore diagnostics。
- `startHost`: optional boolean，預設為 false。true 時 successful build 後會啟動 temporary preview host，並回報 generated-view load status。
- `includeRuntimeDiagnostics`: optional boolean，預設為 false。搭配 `startHost=true` 時，會對 temporary host 重用 `connect`、`get_ui_summary(depthMode="semantic")` 與 `get_layout_info`。這需要 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`。
- `includeScreenshotDiagnostics`: optional boolean，預設為 false。搭配 `startHost=true` 時會啟用 runtime diagnostics，且只有在 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` 與 `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 同時允許時才會要求 screenshot metadata。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

此 tool 只會寫入隔離的 temporary preview directory，compile smoke 後會刪除。Preview project 使用本機 WPF UI stubs，因此測試不依賴 NuGet cache 或 network access。成功結果包含 `buildSucceeded=true`、generated `xaml`、captured `buildOutput` 與 `previewHost` summary。Runtime diagnostics 是 opt-in，會回傳在 `previewHost.runtimeDiagnostics`。Build failure 會回傳 diagnostics，能在可用時對應回 blueprint root 與 renderer template path。

## `repair_ui_blueprint`

將 validation、render、compile 或 preview diagnostics 轉成 blueprint-first repair actions。當 `validate_ui_blueprint`、`render_ui_blueprint` 或 `preview_ui_blueprint` 回傳 issues 後使用。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `diagnosticsJson`: optional diagnostics JSON object 或 array，可使用 render 或 preview result 中的 diagnostics。
- `targetPath`: optional target XAML path suggestion，只用於 render diagnostics。此 tool 不會寫入。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

Response 包含 `repairable`、`generatedXamlPatch=false`、`actionCount` 與 `actions`。Actions 會標示 repair 應落在 blueprint 或 pack renderer template contract。此 tool 不會直接 patch generated XAML。

## `apply_ui_blueprint`

為 UI blueprint 產生 guarded apply plan。預設為 dry-run，讓 Agent 能在任何寫檔前檢查 generated view file path、required resources、package plan 與 binding contract stub。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `projectRoot`: required local WPF project root，用於 path planning 與 write allowlist checks。
- `targetPath`: optional target XAML file path，必須位於 `projectRoot` 內。
- `dryRun`: optional boolean，預設為 true。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

非 dry-run 寫入需要 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`、`WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`，且 `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` 必須 exact match。此 tool 會拒絕 `projectRoot` 外的路徑、更新既有 view 前建立 backup、加入 `WPFDEVTOOLS_BLUEPRINT_SOURCE` header、保留 `WPFDEVTOOLS_SAFE_SLOT` manual-edit markers，且不會執行 NuGet restore。
