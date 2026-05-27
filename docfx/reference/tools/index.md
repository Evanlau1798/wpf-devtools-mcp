# Tool Reference Overview

The server currently exposes 64 tools across eleven categories.

## Categories

1. Process Management
2. Tree & XAML
3. Binding Diagnostics
4. DependencyProperty
5. Style/Template
6. RoutedEvent
7. Interaction
8. Layout
9. MVVM
10. Performance
11. State & Scene Diagnostics

## Recommended order of use

Most real sessions should follow this progression:

Before step 1, confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` includes the reviewed target's exact local absolute executable path; unset or malformed values fail closed before `connect` attaches.

1. `connect()` for the default auto-discovery path
2. `get_active_process`
3. `get_ui_summary` or `get_form_summary` for scene-first context
4. Focused diagnostics
5. Interaction or mutation
6. Verification
7. `ping` only when you need an explicit health check

Use `get_processes(windowFilter)` only when:

- more than one WPF target is available
- you need architecture/elevation details or an explicit candidate list before connecting
- you want to choose a specific `processId` before connecting

If you only need broader auto-discovery, prefer `connect(windowFilter='all')` and keep `get_processes(windowFilter)` for explicit disambiguation or metadata-first selection.

## Categories at a glance

| Category | Typical first call | Why |
| --- | --- | --- |
| Process Management | `connect()` | Auto-discover and connect to the most relevant allowlisted WPF target quickly |
| Tree & XAML | `find_elements` | Perform a compact lookup before expanding a full tree |
| Binding Diagnostics | `get_binding_errors` | Find the most actionable binding failures quickly |
| DependencyProperty | `get_dp_value_source` | Understand precedence and effective values |
| Style/Template | `get_applied_styles` | Explain inherited or implicit visual behavior |
| RoutedEvent | `get_event_handlers` | Investigate event routes and handlers before tracing or firing |
| Interaction | `click_element` | Trigger behavior after locating and validating the correct element |
| Layout | `get_layout_info` | Inspect bounds, desired size, and layout state |
| MVVM | `get_viewmodel` | Inspect data and commands behind a view |
| Performance | `get_render_stats` | Start performance triage |
| State & Scene Diagnostics | `get_ui_summary` | Start with semantic context before using tree-heavy inspection |

Key capabilities worth learning early:

- `select_active_process` and `get_active_process` for explicit process selection when later calls omit `processId`
- `get_focus_state` and `focus_element` for focus-sensitive keyboard and multi-window workflows
- `capture_state_snapshot`, `batch_mutate`, `wait_for_dp_change`, and `restore_state_snapshot` for mutation-safe validation and rollback
- `drain_events` for deterministic reads from the shared runtime event buffer
- `find_elements` for compact lookup with `exact` or case-insensitive `contains` matching
- `get_affected_elements` for a cheap best-effort scan before broad `get_bindings(recursive: true)` inspection
- `get_state_diff`, `get_element_snapshot(elementId)`, `diagnose_visibility`, and `get_interaction_readiness` for scene-level diagnostics that reduce screenshot dependence after a concrete elementId is known
- `get_ui_summary` and `get_form_summary` for semantic subtree summaries before deep inspection or form triage

## Navigation model

- By default, tool responses keep `nextSteps` as the compatibility field for older clients and also include a `navigation` envelope with `recommended`, `alternatives`, `prefetchTools`, and `contextRefs`.
- `nextSteps` is derived from `navigation.recommended`, so both surfaces stay synchronized unless `get_binding_errors` explicitly disables navigation.
- Capable clients may pass `navigation=false` on `get_binding_errors` when they already know the next action and want to reduce response size. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the `get_binding_errors` tool schema today. Do not assume other tool schemas expose that parameter unless they advertise it explicitly.
- `prefetchTools` is advisory only and contains tool names for progressive schema loading.
- `contextRefs` are descriptive JSON only; they are not executable handles or hidden server-side orchestration.

## Response shape notes

- Structured clients should read `result.structuredContent` as the canonical wire payload.
- High-value tool descriptions in `tools/list` are intentionally brief discovery hints; use `wpf://contracts/response` for stable field-level contracts instead of relying on long inline prose.
- `tools/list` advertises exact `outputSchema` shapes for high-value tools such as `connect`, `get_processes`, `get_ui_summary`, `get_element_snapshot(elementId)`, state snapshot/restore, batch mutation, and screenshots. Other tools inherit the common structured payload schema with stable fields such as `success`, `navigation`, and common identifiers. Claude-compatible client smoke tests should validate discovery against these structured-output metadata shapes.
- Need a machine-readable contract? Read MCP resource `wpf://contracts/response`. It publishes the stable detailed WPF payload contract for `structuredContent`, `navigation`, `nextSteps`, `contextRefs`, and the `get_binding_errors` `navigation=false` opt-out beyond the common `tools/list` schema.
- Need canonical tool metadata? Read MCP resource `wpf://contracts/tools`. It publishes the machine-readable JSON manifest for tool names, categories, safety flags, capability tags, and parameter metadata.
- `result.content[0].text` is a compact JSON fallback that preserves high-signal top-level scalar fields and collection counts, not a duplicate transport of the full JSON object. Set `WPFDEVTOOLS_TEXT_FALLBACK_MODE=full` only for legacy text-only MCP clients that require the full JSON payload in `result.content[0].text`; error results include `result.content[0].annotations`.
- Diagnostic tools may also piggyback `pendingEvents` when the session has buffered runtime events. Use `drain_events` when you need an explicit deterministic read of the shared event buffer.

## Generated Contract Snapshot

These values are generated from the runtime MCP contract resources. If a tool is added or renamed, a method signature changes, policy gates move, or response fields change, the documentation tests require this snapshot to be regenerated.

- `wpf://contracts/tools` SHA-256: `c9b25edd66b605e0a5da57306d4dd144d281ab8ae5dbf12dd1f35f09a8db313b`
- `wpf://contracts/response` SHA-256: `4e8582ea490e5136092b0bb76cf6c7387fb7fcc7f7b66ef01b7cd78069f1c578`
- Validation scope: `toolCount`, `name`, `title`, `parameters`, `requiredParameters`, `inputSchemaHash`, `outputSchemaHash`, `capabilityTags`, `policyCapabilityTags`, `annotations`, `parameterConstraints`, `parameterVocabularies`, and `highValueTools`.

Use the category pages for the most important tools, semantics, and gotchas.
