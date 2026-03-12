# 工具參考總覽

目前伺服器共提供 60 個工具，分成 11 個類別。

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

大多數實際工作階段建議採用以下流程：

1. `connect()` 作為預設 auto-discovery 入口
2. `get_active_process`
3. 需要顯式健康檢查時再用 `ping`
4. scene 或 tree 探查
5. diagnostics
6. interaction 或 mutation
7. verification

以下情況再使用 `get_processes(windowFilter)`：

- 系統中同時有多個 WPF 目標
- 你要明確查看背景或前景限定視窗
- 你想先挑選特定 `processId` 再執行 `connect(processId)`

## 類別摘要

| 類別 | 建議第一個呼叫的工具 | 用途 |
| --- | --- | --- |
| Process management | `connect()` | 快速自動發現並連線到最相關的 WPF 目標 |
| Tree and XAML | `find_elements` | 先做精簡查找，再決定是否展開完整 tree |
| Binding diagnostics | `get_binding_errors` | 快速找到最值得先處理的 binding 錯誤 |
| Dependency properties | `get_dp_value_source` | 釐清 precedence 與實際生效值 |
| Style and template | `get_applied_styles` | 解釋 inherited / implicit 視覺行為 |
| Routed events | `get_event_handlers` | 追查事件路徑與 handler |
| Interaction | `click_element` | 定位正確元素後觸發行為 |
| Layout | `get_layout_info` | 檢查邊界、期望尺寸與 layout 狀態 |
| MVVM | `get_viewmodel` | 查看 view 背後的資料與命令 |
| Performance | `get_render_stats` | 開始效能診斷 |
| Scene diagnostics | `get_element_snapshot` | 用單次呼叫收斂常見多工具檢查流程 |

近期值得優先熟悉的新增能力：

- `select_active_process` 與 `get_active_process`：當後續呼叫省略 `processId` 時，用來明確管理目前作用中的目標
- `get_focus_state` 與 `focus_element`：處理鍵盤輸入、多視窗與焦點敏感流程
- `capture_state_snapshot`、`wait_for_dp_change` 與 `restore_state_snapshot`：支援 mutation-safe 驗證、等待 DP 變化與回復狀態
- `find_elements`：支援 `exact` 與不分大小寫的 `contains` 模式，適合在展開 tree 前先做精簡搜尋
- `get_state_diff`、`get_element_snapshot`、`diagnose_visibility`、`get_interaction_readiness`：提供 scene-level 診斷，降低對 screenshot 的依賴
- `get_ui_summary` 與 `get_form_summary`：在深入檢查前，先取得語義化子樹或表單摘要

如需細節、回傳契約與限制，請閱讀各分類頁面。
