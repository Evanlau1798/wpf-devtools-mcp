# 工具總覽

目前伺服器共提供 62 個工具，分成 11 個類別。

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

1. `connect()` 使用預設 auto-discovery 路徑
2. `get_active_process`
3. 只有需要明確健康檢查時再呼叫 `ping`
4. 先做 scene 或 tree discovery
5. 再做 diagnostics
6. 最後進行 interaction 或 mutation
7. 用 verification 收尾

只有在下列情況才優先使用 `get_processes(windowFilter)`：

- 同時存在多個 WPF target
- 需要明確查看背景或前景視窗
- 想在連線前先選定 `processId`

## 類別速覽

| 類別 | 常見第一個呼叫 | 用途 |
| --- | --- | --- |
| Process management | `connect()` | 快速自動探索並連線到最相關的 WPF target |
| Tree and XAML | `find_elements` | 先做緊湊查找，再決定是否展開完整 tree |
| Binding diagnostics | `get_binding_errors` | 先找最有行動價值的 binding 問題 |
| Dependency properties | `get_dp_value_source` | 理解 precedence 與 effective value |
| Style and template | `get_applied_styles` | 說明 inherited 或 implicit 的外觀來源 |
| Routed events | `get_event_handlers` | 追查 event route 與 handler |
| Interaction | `click_element` | 在定位正確 element 後觸發行為 |
| Layout | `get_layout_info` | 檢查 bounds、desired size 與 layout state |
| MVVM | `get_viewmodel` | 檢查 view 背後的資料與 commands |
| Performance | `get_render_stats` | 作為效能初步診斷入口 |
| Scene diagnostics | `get_element_snapshot` | 把常見多步驟檢查收斂成單一 scene summary |

近期值得優先熟悉的功能：

- `select_active_process` 與 `get_active_process`：支援省略 `processId` 的明確 process selection
- `get_focus_state` 與 `focus_element`：支援 focus-sensitive keyboard 與多視窗流程
- `capture_state_snapshot`、`wait_for_dp_change`、`restore_state_snapshot`：支援 mutation-safe 驗證與 rollback
- `find_elements`：支援 `exact` 與不分大小寫的 `contains` 搜尋
- `get_state_diff`、`get_element_snapshot`、`diagnose_visibility`、`get_interaction_readiness`：提供 scene-level diagnostics，降低對 screenshot 的依賴
- `get_ui_summary` 與 `get_form_summary`：在深入檢查前先取得語意化摘要

## Navigation 模型

- 每個工具回應都保留 `nextSteps`，作為舊版 client 的 compatibility field。
- v3 另外加入 `navigation` envelope，包含 `recommended`、`alternatives`、`prefetchTools`、`contextRefs`。
- `nextSteps` 由 `navigation.recommended` 推導，兩種表示法保持同步。
- `prefetchTools` 只是 advisory hint，內容只包含 tool name，供 capable client 做 progressive schema loading。
- `contextRefs` 是 descriptive JSON only，不是 executable handle，也不是隱藏的 server-side orchestration token。

需要更深入的語意與使用注意事項時，請再查看各分類頁面。
