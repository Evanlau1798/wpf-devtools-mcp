# Tool Reference Overview

The server currently exposes 77 tools across twelve categories.

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
12. UI Composer

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

## Start by intent

Use the smallest workflow that answers the question. Prefer scene-level aggregation before tree dumps or screenshots.

| Intent | First tool | Common follow-up | Details |
| --- | --- | --- | --- |
| Confirm what app is connected | `connect` | `get_active_process` | Target access still requires `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`. |
| Understand the current screen | `get_ui_summary` | `find_elements`, then `get_element_snapshot(elementId)` | Good default for agents because it is semantic and compact. |
| Diagnose binding failures | `get_binding_errors` | `get_affected_elements`, `get_bindings`, `get_datacontext_chain` | Keep compact mode unless the summary is insufficient. |
| Explain an unexpected visual value | `get_dp_value_source` | `get_applied_styles`, `get_resource_chain`, `get_triggers` | Use when precedence or styles are unclear. |
| Validate a click or keyboard action | `get_interaction_readiness` | `click_element`, `drain_events`, `get_state_diff` | Use only after a concrete elementId is known. |
| Make rollback-safe changes | `capture_state_snapshot` | `batch_mutate`, `get_state_diff`, `restore_state_snapshot` | Requires the relevant destructive and read gates. |
| Install, compose, render, preview, repair, and apply Composer UIs | `list_ui_block_packs` | `import_ui_block_pack`, `get_ui_block_catalog`, `create_ui_blueprint_draft`, `patch_ui_blueprint_draft`, `compose_ui_blueprint`, `expand_ui_recipe`, `validate_ui_blueprint`, `render_ui_blueprint`, `preview_ui_blueprint`, `repair_ui_blueprint`, `apply_ui_blueprint`, `apply_ui_project_integration` | Dry-runs reviewed project-local pack imports, then builds bounded drafts, discovers, composes, validates, renders, previews, repairs, and guarded-applies pack-defined UIs and their reviewed project integration. |
| Follow a full recipe | See [Common Workflows](../../guides/common-workflows.md) | Follow `navigation.recommended` first | Workflow pages are baselines; tool responses remain authoritative. |

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
| UI Composer | `list_ui_block_packs`, `import_ui_block_pack`, `get_ui_block_catalog`, `create_ui_blueprint_draft`, `patch_ui_blueprint_draft`, `compose_ui_blueprint`, `expand_ui_recipe`, `validate_ui_blueprint`, `render_ui_blueprint`, `preview_ui_blueprint`, `repair_ui_blueprint`, `apply_ui_blueprint`, `apply_ui_project_integration` | Dry-run and import reviewed project-local packs, then build bounded drafts, discover, compose, validate, render, preview, repair, and guarded-apply blueprints plus reviewed project integration |

## Navigation model

- Tool responses keep `nextSteps` as a compatibility field and also include a `navigation` envelope with `recommended`, `alternatives`, `prefetchTools`, and `contextRefs`.
- `nextSteps` is derived from `navigation.recommended` unless a tool explicitly disables navigation.
- `prefetchTools` is advisory only and contains tool names for progressive schema loading.
- `contextRefs` are descriptive JSON only; they are not executable handles.

## Response shape notes

- Structured clients should read `result.structuredContent` as the canonical wire payload.
- `tools/list` advertises exact `outputSchema` shapes for high-value tools such as `connect`, `get_processes`, `get_ui_summary`, `get_element_snapshot(elementId)`, state snapshot/restore, batch mutation, and screenshots. Other tools inherit the common structured payload schema with stable fields such as `success`, `navigation`, and common identifiers. Claude-compatible clients should validate discovery against these structured-output metadata shapes.
- Use MCP resource `wpf://contracts/response` for the stable detailed WPF payload contract.
- Use MCP resource `wpf://contracts/tools` for canonical machine-readable tool names, categories, safety flags, capability tags, parameter metadata, and reflection-backed parameter `constraints`.
- If a client bridge truncates a contract resource, read the compact `wpf://contracts/index`, request the advertised `wpf://contracts/{contractId}/chunks/{offset}/{length}` ranges sequentially at no more than 16 KiB, concatenate decoded UTF-8 bytes without transformation, and verify `byteLength` plus SHA-256 before parsing JSON.
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

- `wpf://contracts/tools` SHA-256: `afaa0d30762c2739caaf24c99e50eed99c19c38de571e545ea37c46b94b04662`
- `wpf://contracts/response` SHA-256: `4cd347f6945d0d7956cc7ec13c3ec75c87eedad79b85282f903bb902e3dcaadb`
- Validation scope: `toolCount`, `name`, `title`, `parameters`, parameter `constraints`, `requiredParameters`, `inputSchemaHash`, `outputSchemaHash`, `capabilityTags`, `policyCapabilityTags`, `annotations`, `parameterConstraints`, `parameterVocabularies`, and `highValueTools`.

Use the category pages for the most important tools, semantics, and gotchas.
