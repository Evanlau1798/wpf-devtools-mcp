# 互動、事件、版面配置與效能工具

## Interaction

- `click_element`
- `drag_and_drop`
- `scroll_to_element`
- `simulate_keyboard`
- `element_screenshot`
- `get_focus_state`
- `focus_element`
- `capture_state_snapshot`
- `restore_state_snapshot`

當流程和鍵盤輸入、預設按鈕、tab 導覽或多視窗切換有關時，`get_focus_state` 與 `focus_element` 會很重要。

當你要做可能需要回復的 UI mutation 時，建議先用 `capture_state_snapshot`，結束後再視需要呼叫 `restore_state_snapshot`。

互動類工具的回應現在也會帶出 `nextSteps` 與 `navigation`。當回應已提供 follow-up guidance 時，請優先遵循它，而不是回到固定的手工驗證清單。

## Routed Events

- `trace_routed_events`
- `get_event_handlers`
- `fire_routed_event`

`fire_routed_event` 對 route 分析很有用，但它不是所有真實使用者輸入的通用替代品。

若你先用 `trace_routed_events(mode: "start")` 建立 trace session，再做互動，後續通常應先呼叫 `trace_routed_events(mode: "get")` 讀回事件資料。

## Layout

- `get_layout_info`
- `highlight_element`
- `get_clipping_info`
- `invalidate_layout`

## MVVM

- `get_viewmodel`
- `get_commands`
- `execute_command`
- `modify_viewmodel`
- `get_validation_errors`

## Performance

- `get_render_stats`
- `find_binding_leaks`
- `measure_element_render_time`
- `get_visual_count`

## 安全使用模式

1. 先檢查。
2. 在改變 UI 狀態前先呼叫 `capture_state_snapshot`。
3. 若流程受焦點影響，先用 `get_focus_state` 與 `focus_element` 確認目標。
4. 做一次互動。
5. 先依工具回應中的 `navigation.recommended` 或 `nextSteps` 驗證。
6. 若目前 session 有 active snapshot，通常應先呼叫 `get_state_diff`。
7. 若目前 session 有 active routed-event trace，通常應先呼叫 `trace_routed_events(mode: "get")`。
8. 若需要回復或保持 app 不變，呼叫 `restore_state_snapshot`。
9. 避免在單一步驟中疊很多 mutation。
