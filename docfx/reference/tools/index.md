# Tool Reference Overview

The server currently exposes 63 tools across eleven categories.

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

1. `connect()` for the default auto-discovery path
2. `get_active_process`
3. `ping` when you need an explicit health check
4. Scene or tree discovery
5. Diagnostics
6. Interaction or mutation
7. Verification

Use `get_processes(windowFilter)` only when:

- more than one WPF target is available
- you need to inspect background or foreground-only windows explicitly
- you want to choose a specific `processId` before connecting

## Categories at a glance

| Category | Typical first call | Why |
| --- | --- | --- |
| Process management | `connect()` | Auto-discover and connect to the most relevant WPF target quickly |
| Tree and XAML | `find_elements` | Perform a compact lookup before expanding a full tree |
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
- `capture_state_snapshot`, `wait_for_dp_change`, and `restore_state_snapshot` for mutation-safe validation and rollback
- `find_elements` for compact lookup with `exact` or case-insensitive `contains` matching
- `get_state_diff`, `get_element_snapshot`, `diagnose_visibility`, and `get_interaction_readiness` for scene-level diagnostics that reduce screenshot dependence
- `get_ui_summary` and `get_form_summary` for semantic subtree summaries before deep inspection or form triage

## Navigation model

- Every tool response keeps `nextSteps` as the compatibility field for older clients.
- v3 also adds a `navigation` envelope with `recommended`, `alternatives`, `prefetchTools`, and `contextRefs`.
- `nextSteps` is derived from `navigation.recommended`, so both surfaces stay synchronized.
- `prefetchTools` is advisory only and contains tool names for progressive schema loading.
- `contextRefs` are descriptive JSON only; they are not executable handles or hidden server-side orchestration.

Use the category pages for the most important tools, semantics, and gotchas.
