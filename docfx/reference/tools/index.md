# Tool Reference Overview

The server currently exposes 44 tools across ten categories.

## Categories

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

## Recommended order of use

Most real sessions should follow this progression:

1. `get_processes`
2. `connect`
3. `ping`
4. Tree discovery
5. Diagnostics
6. Interaction or mutation
7. Verification

## Categories at a glance

| Category | Typical first call | Why |
| --- | --- | --- |
| Process management | `get_processes` | Discover valid targets and architecture |
| Tree and XAML | `get_visual_tree` | Obtain `elementId` values and structure |
| Binding diagnostics | `get_binding_errors` | Find the most actionable binding failures quickly |
| Dependency properties | `get_dp_value_source` | Understand precedence and effective values |
| Style and template | `get_applied_styles` | Explain inherited or implicit visual behavior |
| Routed events | `get_event_handlers` | Investigate event routes and handlers |
| Interaction | `click_element` | Trigger behavior after locating the correct element |
| Layout | `get_layout_info` | Inspect bounds, desired size, and layout state |
| MVVM | `get_viewmodel` | Inspect data and commands behind a view |
| Performance | `get_render_stats` | Start performance triage |

Use the category pages for the most important tools, semantics, and gotchas.