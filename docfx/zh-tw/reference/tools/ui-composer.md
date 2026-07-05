# UI Composer 工具

UI Composer 工具用於本機 Composer extension pack 與 blueprint 輸入。它們不檢查正在執行的 WPF target，應在 catalog、validation、rendering 或 apply workflow 前使用。

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

Catalog entries 只包含 source hint paths，不會把第三方 source code 複製進 tool output。

## `validate_ui_blueprint`

依照已安裝 Composer pack contracts 驗證 UI blueprint JSON。請在 `list_ui_block_packs` 與 `get_ui_block_catalog` 之後、rendering XAML 或 apply generated UI 之前使用。

Request options:

- `blueprintJson`: required UI blueprint JSON，`schemaVersion` 必須是 `wpfdevtools.ui-blueprint.v1`。
- `projectRoot`: optional WPF project root。提供時，會從 `<projectRoot>/.wpfdevtools/packs` 探索 project-local packs。
- `localAppDataRoot`: optional user-global discovery root。省略時，server 會使用目前使用者的 LocalApplicationData path。

完成 validation 呼叫時 response 會維持 `success=true`，並用 `valid` 表示 blueprint 是否有效。Validation issues 會包含 `jsonPath`、`code`、`message`、`repairSuggestion`，以及相關的 `allowedKinds` 或 `allowedValues`。
