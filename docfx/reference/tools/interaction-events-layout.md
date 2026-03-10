# Interaction, Event, Layout, and Performance Tools

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

`get_focus_state` and `focus_element` matter whenever keyboard input, default buttons, tab navigation, or multiple windows are involved.

`capture_state_snapshot` and `restore_state_snapshot` are the preferred guard rails before trying UI mutations that may need rollback.

## Routed events

- `trace_routed_events`
- `get_event_handlers`
- `fire_routed_event`

`fire_routed_event` is useful for route analysis. It is not a universal substitute for real user input.

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

## Safe usage pattern

1. Inspect first.
2. Call `capture_state_snapshot` before changing UI state.
3. Use `get_focus_state` and `focus_element` before keyboard-sensitive actions.
4. Interact once.
5. Verify immediately.
6. Use `restore_state_snapshot` if the workflow requires rollback or if you need to leave the app unchanged.
7. Avoid stacking many mutations into one agent step.
