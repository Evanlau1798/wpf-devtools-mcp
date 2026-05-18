# 工具總覽

目前伺服器共提供 64 個工具，分成 11 個類別。

## 類別

1. Process management
2. Tree and XAML
3. Binding diagnostics
4. Dependency properties
5. Style and template
6. Routed events
7. Interaction
8. Layout
9. MVVM
10. Performance
11. Scene diagnostics

## 建議使用順序

大多數實際工作流程可依照下列順序進行：

在第 1 步前，先確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含已審查 target 的 exact absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。

1. `connect()` 使用預設 auto-discovery 路徑
2. `get_active_process`
3. `get_ui_summary` 或 `get_form_summary` 作為 scene-first context
4. 聚焦式 diagnostics
5. interaction 或 mutation
6. verification
7. 只有需要明確健康檢查時才呼叫 `ping`

只有在下列情況才優先使用 `get_processes(windowFilter)`：

- 同時存在多個 WPF target
- 需要在連線前先取得架構、權限或明確候選清單
- 想在連線前先選定 `processId`

如果你只是想放寬 auto-discovery 範圍，優先使用 `connect(windowFilter='all')`；`get_processes(windowFilter)` 應保留給明確的 disambiguation 或 metadata-first selection。

## 類別速覽

| 類別 | 常見第一個呼叫 | 用途 |
| --- | --- | --- |
| Process management | `connect()` | 快速自動探索並連線到最相關的已 allowlist WPF target |
| Tree and XAML | `find_elements` | 先做緊湊查找，再決定是否展開完整 tree |
| Binding diagnostics | `get_binding_errors` | 先找最有行動價值的 binding 問題 |
| Dependency properties | `get_dp_value_source` | 理解 precedence 與 effective value |
| Style and template | `get_applied_styles` | 說明 inherited 或 implicit 的外觀來源 |
| Routed events | `get_event_handlers` | 在 trace 或 fire 之前先追查 event route 與 handler |
| Interaction | `click_element` | 在定位並驗證正確 element 後觸發行為 |
| Layout | `get_layout_info` | 檢查 bounds、desired size 與 layout state |
| MVVM | `get_viewmodel` | 檢查 view 背後的資料與 commands |
| Performance | `get_render_stats` | 作為效能初步診斷入口 |
| Scene diagnostics | `get_ui_summary` | 在使用 tree-heavy inspection 前先取得語意化上下文 |

建議優先熟悉的重點功能：

- `select_active_process` 與 `get_active_process`：支援省略 `processId` 的明確 process selection
- `get_focus_state` 與 `focus_element`：支援 focus-sensitive keyboard 與多視窗流程
- `capture_state_snapshot`、`batch_mutate`、`wait_for_dp_change`、`restore_state_snapshot`：支援 mutation-safe 驗證與 rollback
- `drain_events`：用於對 shared runtime event buffer 做 deterministic read
- `find_elements`：支援 `exact` 與不分大小寫的 `contains` 搜尋
- `get_affected_elements`：在大範圍 `get_bindings(recursive: true)` 之前，先做低成本的 best-effort candidate scan
- `get_state_diff`、`get_element_snapshot(elementId)`、`diagnose_visibility`、`get_interaction_readiness`：在已取得具體 `elementId` 後提供 scene-level diagnostics，降低對 screenshot 的依賴
- `get_ui_summary` 與 `get_form_summary`：在深入檢查前先取得語意化摘要

## Navigation 模型

- 預設情況下，工具回應會保留 `nextSteps` 作為舊版 client 的 compatibility field，並同時加入 `navigation` envelope，包含 `recommended`、`alternatives`、`prefetchTools`、`contextRefs`。
- 除非 `get_binding_errors` 明確停用 navigation，否則 `nextSteps` 都會由 `navigation.recommended` 推導，兩種表示法保持同步。
- 若 client 已經知道下一步，且希望縮小回應大小，具備額外 optional args 傳遞能力的 client 可在 `get_binding_errors` 單次呼叫上傳入 `navigation=false`；schema-driven client 可以在這個工具上依賴這個 opt-out，因為它今天已經公告在 tool schema 中。不要假設其他 tool schema 今天都會明確公告這個參數。
- `prefetchTools` 只是 advisory hint，內容只包含 tool name，供 capable client 做 progressive schema loading。
- `contextRefs` 是 descriptive JSON only，不是 executable handle，也不是隱藏的 server-side orchestration token。

## 回應形狀補充

- 支援 structured content 的 client 應以 `structuredContent` 作為正式 payload。
- `tools/list` 會公告共用 `result.structuredContent` payload 欄位的 `outputSchema`，包含 `success`、`navigation`，以及 `processId` 這類常見識別欄位。Claude-compatible client smoke test 應針對這個 structured-output metadata shape 驗證 discovery。
- 如果 client 需要 machine-readable contract，請直接讀取 MCP resource `wpf://contracts/response`。它會提供比共用 `tools/list` schema 更完整的 WPF payload contract，涵蓋 `structuredContent`、`navigation`、`nextSteps`、`contextRefs`，以及 `get_binding_errors` `navigation=false` opt-out。
- `content[0].text` 是精簡的 JSON fallback，會保留高訊號的 top-level scalar 欄位與集合計數摘要，而不是完整 JSON 的重複傳輸。只有 legacy text-only MCP client 需要在 `content[0].text` 取得完整 JSON 時，才設定 `WPFDEVTOOLS_TEXT_FALLBACK_MODE=full`。
- 若 session 內已存在 buffered runtime event，部分 diagnostic 工具也可能在回應中 piggyback `pendingEvents`。若你需要明確且 deterministic 的 event read step，請改用 `drain_events`。

需要更深入的語意與使用注意事項時，請再查看各分類頁面。
