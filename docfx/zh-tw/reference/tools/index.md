# 工具總覽

目前 server 提供 70 個工具，分成 12 個類別。

## 類別

1. Process Management
2. Tree & XAML
3. Binding Diagnostics
4. DependencyProperty
5. Style/Template
6. RoutedEvent
7. Interaction
8. Layout
9. MVVM
10. Performance
11. State & Scene Diagnostics
12. UI Composer

## 建議使用順序

在第 1 步前，請確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含 reviewed target 的 exact local absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。

1. `connect()` 使用預設 auto-discovery path
2. `get_active_process`
3. `get_ui_summary` 或 `get_form_summary` 作為 scene-first context
4. focused diagnostics
5. 只有 policy gates 啟用後才做 interaction 或 mutation
6. verification
7. 只有需要明確健康檢查時才呼叫 `ping`

只有在存在多個 WPF target、需要 architecture/elevation details，或需要連線前明確選擇 `processId` 時，才使用 `get_processes(windowFilter)`。

如果只是需要擴大 auto-discovery 範圍，請優先用 `connect(windowFilter='all')`；`get_processes(windowFilter)` 保留給明確 disambiguation 或 metadata-first selection。

## 先用意圖選工具

使用能回答問題的最小 workflow。先用 scene-level aggregation，再考慮 dump tree 或 screenshot。

| 意圖 | 第一個 tool | 常見 follow-up | 補充 |
| --- | --- | --- | --- |
| 確認目前連到哪個 app | `connect` | `get_active_process` | Target access 仍需要 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`。 |
| 理解目前畫面 | `get_ui_summary` | `find_elements`，再 `get_element_snapshot(elementId)` | 對 Agent 來說是好的預設，因為語意化且 compact。 |
| 診斷 binding failures | `get_binding_errors` | `get_affected_elements`、`get_bindings`、`get_datacontext_chain` | 除非 summary 不足，先維持 compact mode。 |
| 解釋非預期 visual value | `get_dp_value_source` | `get_applied_styles`、`get_resource_chain`、`get_triggers` | 用於 precedence 或 style 不清楚時。 |
| 驗證 click 或 keyboard action | `get_interaction_readiness` | `click_element`、`drain_events`、`get_state_diff` | 只有在已知具體 `elementId` 後才使用。 |
| 做 rollback-safe changes | `capture_state_snapshot` | `batch_mutate`、`get_state_diff`、`restore_state_snapshot` | 需要對應 destructive 與 read gates。 |
| 展開、render 並 apply Composer recipes | `list_ui_block_packs` | `get_ui_block_catalog`、`expand_ui_recipe`、`validate_ui_blueprint`、`render_ui_blueprint`、`apply_ui_blueprint` | 先列出 installed UI packs 與 recipes、展開 starter recipe、驗證 blueprint JSON、dry-run XAML rendering，再產生 guarded apply plan。 |
| 跟完整 recipe | 看 [常見工作流程](../../guides/common-workflows.md) | 先跟 `navigation.recommended` | Workflow pages 是 baseline；tool response 仍是權威。 |

## 類別速覽

| 類別 | 常見第一個呼叫 | 用途 |
| --- | --- | --- |
| Process Management | `connect()` | 快速探索並連線到最相關的 allowlisted WPF target |
| Tree & XAML | `find_elements` | 先做 compact lookup，再決定是否展開完整 tree |
| Binding Diagnostics | `get_binding_errors` | 先找最有行動價值的 binding failure |
| DependencyProperty | `get_dp_value_source` | 理解 precedence 與 effective values |
| Style/Template | `get_applied_styles` | 說明 inherited 或 implicit visual behavior |
| RoutedEvent | `get_event_handlers` | 在 trace 或 fire 前先追查 event route 與 handler |
| Interaction | `click_element` | 在定位並驗證 element 後觸發行為 |
| Layout | `get_layout_info` | 檢查 bounds、desired size 與 layout state |
| MVVM | `get_viewmodel` | 檢查 view 背後的資料與 commands |
| Performance | `get_render_stats` | 作為 performance triage 起點 |
| State & Scene Diagnostics | `get_ui_summary` | 在 tree-heavy inspection 前取得 semantic context |
| UI Composer | `list_ui_block_packs`、`get_ui_block_catalog`、`expand_ui_recipe`、`validate_ui_blueprint`、`render_ui_blueprint`、`apply_ui_blueprint` | 探索 UI block packs、檢查 recipes 與 composition rules，並展開、驗證、render 及 guarded-apply blueprints |

## Navigation model

- Tool response 會保留 `nextSteps` 作為 compatibility field，並包含 `navigation` envelope：`recommended`、`alternatives`、`prefetchTools` 與 `contextRefs`。
- 除非 tool 明確停用 navigation，`nextSteps` 會由 `navigation.recommended` 推導。
- `prefetchTools` 只是 advisory tool-name hint。
- `contextRefs` 是 descriptive JSON，不是 executable handle。

## Response shape notes

- 支援 structured content 的 client 應讀取 `result.structuredContent` 作為 canonical wire payload。
- `tools/list` 會針對 `connect`、`get_processes`、`get_ui_summary`、`get_element_snapshot(elementId)`、state snapshot/restore、batch mutation 與 screenshots 等 high-value tools 公告精確的 `outputSchema`。其他工具仍繼承共用 structured payload schema，包含 `success`、`navigation` 與常見識別欄位。Claude-compatible clients 應針對這些 structured-output metadata shapes 驗證 discovery。
- 使用 MCP resource `wpf://contracts/response` 取得穩定詳細的 WPF payload contract。
- 使用 MCP resource `wpf://contracts/tools` 取得 canonical tool names、categories、safety flags、capability tags 與 parameter metadata。
- `result.content[0].text` 是 compact JSON fallback，會保留高訊號 top-level scalar fields 與集合計數，而不是完整 JSON 的重複傳輸。只有 legacy client 需要時才設定 `WPFDEVTOOLS_TEXT_FALLBACK_MODE=full`。
- Error result 會包含 `result.content[0].annotations`，並保留 `result.structuredContent` 供 machine-readable handling 使用。
- Diagnostic tools 可能包含 `pendingEvents`；需要 deterministic event read 時請使用 `drain_events`。

## Contract validation scope

DocFX validation script 會優先從 canonical tool manifest 驗證 tool-name coverage，並在輕量 validation fixture 中使用 structured source-attribute fallback，同時檢查 listed tool names 不過期。Unit documentation tests 會驗證下方 generated contract snapshot hashes。Validation script 不會重新產生或完整驗證 parameter list、output schema 或 capability tags。

Parameter metadata、policy gates 與 output schemas 請以 runtime resources 為準：

- `wpf://contracts/tools`
- `wpf://contracts/response`

如果 tool signature、policy gate 或 response schema 變更，請在同一個 PR 更新相關 prose 與 category pages。

## Generated Contract Snapshot

這些值由 runtime MCP contract resources 產生。當 tool 新增或改名、method signature 變更、policy gate 移動，或 response fields 變更時，文件測試會要求同步更新此 snapshot。

- `wpf://contracts/tools` SHA-256: `b238b266da3bef4dbda2db14fee6fdef7fd6e7e0591d4362b4b2565924ade6e1`
- `wpf://contracts/response` SHA-256: `31d58dc9e354a359cf3e70631ee29c3e20b1b86a278b01deabc0adf38fe7c3f5`
- Validation scope: `toolCount`、`name`、`title`、`parameters`、`requiredParameters`、`inputSchemaHash`、`outputSchemaHash`、`capabilityTags`、`policyCapabilityTags`、`annotations`、`parameterConstraints`、`parameterVocabularies` 與 `highValueTools`。

需要更深入的語意與使用注意事項時，請再查看各分類頁面。
