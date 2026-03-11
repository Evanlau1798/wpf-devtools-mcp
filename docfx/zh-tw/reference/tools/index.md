# 工具參考總覽

目前伺服器共提供 52 個工具，分成十個類別。

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

## 建議使用順序

大多數實際工作流程建議依照下列順序：

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
| Process management | `get_processes` | 找出可用目標、架構與連線限制 |
| Tree and XAML | `find_elements` | 在展開完整 tree 前先做精準元素查找 |
| Binding diagnostics | `get_binding_errors` | 優先找到最具行動性的 binding 問題 |
| Dependency properties | `get_dp_value_source` | 釐清 precedence 與目前有效值來源 |
| Style and template | `get_applied_styles` | 說明 implicit 或 inherited 的視覺行為 |
| Routed events | `get_event_handlers` | 追查 event route 與 handler |
| Interaction | `click_element` | 找到正確元素後觸發互動 |
| Layout | `get_layout_info` | 檢查尺寸、位置與 layout 狀態 |
| MVVM | `get_viewmodel` | 檢查 ViewModel 資料與 command |
| Performance | `get_render_stats` | 作為效能診斷的起點 |

最近值得優先熟悉的新增能力：

- `select_active_process` 與 `get_active_process`：在後續呼叫省略 `processId` 時，明確管理目前的目標程序
- `get_focus_state` 與 `focus_element`：處理焦點敏感的鍵盤與多視窗流程
- `capture_state_snapshot` 與 `restore_state_snapshot`：支援可回復的 mutation 驗證
- `find_elements`：在展開完整 tree 前，先用 type/name/automationId/property value 做精準查找

請搭配各分類頁面閱讀重要語意、使用順序與常見陷阱。
