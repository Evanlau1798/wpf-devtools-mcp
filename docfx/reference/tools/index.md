# Tool Reference Overview

The server currently exposes 59 tools across eleven categories.

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
11. Scene diagnostics

## Recommended order of use

Most real sessions should follow this progression:

1. `get_processes`
2. `connect`
3. `select_active_process`
4. `get_active_process`
5. `ping`
6. Tree discovery
7. Diagnostics
8. Interaction or mutation
9. Verification

## Categories at a glance

| Category | Typical first call | Why |
| --- | --- | --- |
| Process management | `get_processes` | Discover valid targets, architecture, and connection constraints |
| Tree and XAML | `find_elements` | Perform a compact exact-match lookup before expanding a full tree |
| Binding diagnostics | `get_binding_errors` | Find the most actionable binding failures quickly |
| Dependency properties | `get_dp_value_source` | Understand precedence and effective values |
| Style and template | `get_applied_styles` | Explain inherited or implicit visual behavior |
| Routed events | `get_event_handlers` | Investigate event routes and handlers |
| Interaction | `click_element` | Trigger behavior after locating the correct element |
| Layout | `get_layout_info` | Inspect bounds, desired size, and layout state |
| MVVM | `get_viewmodel` | Inspect data and commands behind a view |
| Performance | `get_render_stats` | Start performance triage |
| Scene diagnostics | `get_element_snapshot` | Collapse common multi-call inspection flows into one scene summary |

Recent additions worth learning early:

- `select_active_process` and `get_active_process` for explicit process selection when later calls omit `processId`
- `get_focus_state` and `focus_element` for focus-sensitive keyboard and multi-window workflows
- `capture_state_snapshot` and `restore_state_snapshot` for mutation-safe validation and rollback
- `find_elements` for compact exact-match lookup before full tree inspection
- `get_state_diff`, `get_element_snapshot`, `diagnose_visibility`, and `get_interaction_readiness` for scene-level diagnostics that reduce screenshot dependence
- `get_ui_summary` and `get_form_summary` for semantic subtree summaries before deep inspection or form triage

Use the category pages for the most important tools, semantics, and gotchas.
