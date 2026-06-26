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

Before step 1, confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` includes the reviewed target's exact local absolute executable path; unset or malformed values fail closed before `connect` attaches.

1. `connect()` for the default auto-discovery path
2. `get_active_process`
3. `get_ui_summary` or `get_form_summary` for scene-first context
4. Focused diagnostics
5. Interaction or mutation only when policy gates are enabled
6. Verification
7. `ping` only when you need an explicit health check

Use `get_processes(windowFilter)` only when more than one WPF target is available, when you need architecture/elevation details, or when you need an explicit `processId` before connecting.

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

## Navigation model

- Tool responses keep `nextSteps` as a compatibility field and also include a `navigation` envelope with `recommended`, `alternatives`, `prefetchTools`, and `contextRefs`.
- `nextSteps` is derived from `navigation.recommended` unless a tool explicitly disables navigation.
- `prefetchTools` is advisory only and contains tool names for progressive schema loading.
- `contextRefs` are descriptive JSON only; they are not executable handles.

## Response shape notes

- Structured clients should read `result.structuredContent` as the canonical wire payload.
- `tools/list` advertises exact `outputSchema` shapes for high-value tools such as `connect`, `get_processes`, `get_ui_summary`, `get_element_snapshot(elementId)`, state snapshot/restore, batch mutation, and screenshots. Other tools inherit the common structured payload schema with stable fields such as `success`, `navigation`, and common identifiers. Claude-compatible clients should validate discovery against these structured-output metadata shapes.
- Use MCP resource `wpf://contracts/response` for the stable detailed WPF payload contract.
- Use MCP resource `wpf://contracts/tools` for canonical machine-readable tool names, categories, safety flags, capability tags, and parameter metadata.
- `result.content[0].text` is a compact JSON fallback that preserves high-signal top-level scalar fields and collection counts, not a duplicate transport of the full JSON object. Set `WPFDEVTOOLS_TEXT_FALLBACK_MODE=full` only for legacy clients that require it.
- Error results include `result.content[0].annotations` while preserving `result.structuredContent` for machine-readable handling.
- Diagnostic tools may include `pendingEvents`; use `drain_events` when you need a deterministic read of the shared runtime event buffer.

## Contract validation scope

The DocFX validation script verifies tool-name coverage from the canonical tool manifest when available, with a structured source-attribute fallback for lightweight validation fixtures, and checks that no listed tool names are stale. Unit documentation tests validate the generated contract snapshot hashes shown below. The validation script does not regenerate or fully validate parameter lists, output schemas, or capability tags.

For parameter metadata, policy gates, and output schemas, use the runtime resources:

- `wpf://contracts/tools`
- `wpf://contracts/response`

If a tool signature, policy gate, or response schema changes, update the relevant prose and category pages in the same PR.

## Generated Contract Snapshot

These values are generated from the runtime MCP contract resources. If a tool is added or renamed, a method signature changes, policy gates move, or response fields change, the documentation tests require this snapshot to be regenerated.

- `wpf://contracts/tools` SHA-256: `150b755cb01ac147c367637c68f3a76a04648e29db604d3776db0828ad731dcc`
- `wpf://contracts/response` SHA-256: `456920009a47782c3e629b69fb132f4c101e40de0479717fb2643500f69a0378`
- Validation scope: `toolCount`, `name`, `title`, `parameters`, `requiredParameters`, `inputSchemaHash`, `outputSchemaHash`, `capabilityTags`, `policyCapabilityTags`, `annotations`, `parameterConstraints`, `parameterVocabularies`, and `highValueTools`.

Use the category pages for the most important tools, semantics, and gotchas.
