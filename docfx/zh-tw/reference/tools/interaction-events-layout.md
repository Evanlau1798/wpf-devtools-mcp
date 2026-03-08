# 互動、事件、版面配置與效能工具

## Interaction

- `click_element`
- `drag_and_drop`
- `scroll_to_element`
- `simulate_keyboard`
- `element_screenshot`

## Routed Events

- `trace_routed_events`
- `get_event_handlers`
- `fire_routed_event`

`fire_routed_event` 對 route 分析很有用，但它不是所有真實使用者輸入的通用替代品。

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
2. 做一次互動。
3. 立刻驗證。
4. 避免在單一步驟中疊很多 mutation。
