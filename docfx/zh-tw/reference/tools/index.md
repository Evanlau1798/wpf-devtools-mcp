# 工具總覽

目前 server 共提供 49 個工具，分成十個類別。

## 類別

1. 處理程序管理
2. Tree 與 XAML
3. Binding 診斷
4. Dependency Properties
5. Style 與 Template
6. Routed Events
7. Interaction
8. Layout
9. MVVM
10. Performance

## 建議的使用順序

大多數真實 session 都建議遵循以下節奏：

1. `get_processes`
2. `connect`
3. `ping`
4. Tree discovery
5. Diagnostics
6. Interaction 或 mutation
7. Verification

## 各類別速覽

| 類別 | 常見第一個呼叫 | 為什麼 |
| --- | --- | --- |
| 處理程序管理 | `get_processes` | 先找出可連線的目標與架構 |
| Tree 與 XAML | `get_visual_tree` | 先取得 `elementId` 與結構 |
| Binding 診斷 | `get_binding_errors` | 快速找出最可操作的 binding 失敗 |
| Dependency Properties | `get_dp_value_source` | 理解 precedence 與有效值來源 |
| Style 與 Template | `get_applied_styles` | 說明繼承與 implicit 視覺行為 |
| Routed Events | `get_event_handlers` | 檢查事件路徑與 handler |
| Interaction | `click_element` | 在定位正確元素後觸發行為 |
| Layout | `get_layout_info` | 檢查 bounds、desired size 與 layout 狀態 |
| MVVM | `get_viewmodel` | 檢查 view 背後的資料與指令 |
| Performance | `get_render_stats` | 作為效能 triage 起點 |

最近新增、建議優先熟悉的工具：

- `get_focus_state` 與 `focus_element`，適合焦點敏感與多視窗工作流
- `capture_state_snapshot` 與 `restore_state_snapshot`，適合 mutation 前後的安全回復

若要了解各類別中最重要的工具、語意與常見陷阱，請繼續閱讀各分類頁面。
