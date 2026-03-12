# 工具參考總覽

目前伺服器共提供 58 個工具，分屬 11 個類別。

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

多數實際工作流程可依下列順序進行：

1. `get_processes`
2. `connect`
3. `select_active_process`
4. `get_active_process`
5. `ping`
6. Tree discovery
7. Diagnostics
8. Interaction or mutation
9. Verification

## 類別速覽

| 類別 | 建議起手工具 | 用途 |
| --- | --- | --- |
| Process management | `get_processes` | 找出可連線目標、架構與權限限制 |
| Tree and XAML | `find_elements` | 先做精準搜尋，再決定是否展開完整 tree |
| Binding diagnostics | `get_binding_errors` | 快速定位最有價值的 binding 問題 |
| Dependency properties | `get_dp_value_source` | 判斷 precedence 與有效值來源 |
| Style and template | `get_applied_styles` | 解釋 implicit / inherited 樣式效果 |
| Routed events | `get_event_handlers` | 檢查事件路徑與 handler 綁定 |
| Interaction | `click_element` | 在確認目標後觸發互動行為 |
| Layout | `get_layout_info` | 檢查大小、位置與 layout 狀態 |
| MVVM | `get_viewmodel` | 檢視資料與命令狀態 |
| Performance | `get_render_stats` | 啟動效能診斷 |
| Scene diagnostics | `get_element_snapshot` | 用單次呼叫聚合常見診斷資訊 |

近期新增且建議優先熟悉的能力：

- `select_active_process` 與 `get_active_process`：在後續省略 `processId` 前先固定作用中的目標程序
- `get_focus_state` 與 `focus_element`：處理焦點敏感與多視窗工作流程
- `capture_state_snapshot` 與 `restore_state_snapshot`：在 mutation 前後保留可回復狀態
- `find_elements`：在展開完整 tree 前先做精準搜尋
- `get_state_diff`、`get_element_snapshot`、`diagnose_visibility`、`get_interaction_readiness`：以場景級診斷取代大量截圖與多次交叉查詢
- `get_ui_summary` 與 `get_form_summary`：在深度檢查前，先取得語意化的子樹與表單摘要

其餘細節與語意差異請參閱各分類頁面。
