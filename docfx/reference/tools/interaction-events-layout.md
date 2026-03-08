# Interaction, Event, Layout, and Performance Tools

## Interaction

- `click_element`
- `drag_and_drop`
- `scroll_to_element`
- `simulate_keyboard`
- `element_screenshot`

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
2. Interact once.
3. Verify immediately.
4. Avoid stacking many mutations into one agent step.