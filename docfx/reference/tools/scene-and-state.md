# Scene And State Diagnostic Tools

Use these tools after `connect()` to get semantic runtime state before expanding large trees or taking screenshots. The recommended order is summary, focused snapshot, readiness or visibility diagnosis, then snapshot-backed mutation and diff only when the session needs to change runtime state.

## `get_ui_summary`

Purpose: summarize the current WPF window or subtree as user-facing semantic nodes.

Parameters:

- `processId` optional after an active process is selected.
- `elementId` optional subtree root. Omit to summarize the root window.
- `depth` optional traversal budget.
- `depthMode` optional, usually `semantic`.
- `summaryOnly` optional. Use `true` when the agent only needs text context.

Output fields include `rootElementId`, `semanticNodeCount`, `summaryText`, and `nodes`. Use this before tree-heavy tools when the goal is screen understanding.

Example:

```json
{ "depthMode": "semantic", "summaryOnly": true }
```

Recovery path: if the summary identifies a concrete element, continue with `get_element_snapshot(elementId)` before requesting a full visual tree.

## `get_form_summary`

Purpose: summarize input controls, labels, validation, and command readiness inside a form-like subtree.

Parameters:

- `processId` optional after an active process is selected.
- `elementId` optional form scope. Omit to inspect the root window.
- `includeFramework` optional. Keep `false` for user-facing controls only.

Output fields include `inputs`, `commands`, `summary`, `summary.validationSubmittable`, `summary.interactionSubmittable`, and `summary.isSubmittable`.

Example:

```json
{ "elementId": "ProfileForm_2" }
```

Recovery path: if the form is not submittable, inspect the listed input or command with `get_element_snapshot(elementId)`, `diagnose_visibility`, or `get_interaction_readiness`.

## `get_element_snapshot` (requires `elementId`)

Purpose: gather common diagnostics for one runtime element in a single response.

Parameters:

- `elementId` required.
- `processId` optional after an active process is selected.
- `includeProperties` optional extra DependencyProperty probes appended to the default property set.

Output fields include identity, selected properties, bindings, validation errors, style summary, layout summary, and DataContext type. This is the preferred drill-down after `get_ui_summary` or `find_elements`.

Example:

```json
{ "elementId": "SaveButton_7", "includeProperties": ["IsEnabled", "Visibility"] }
```

Recovery path: if a specific value is still unclear, follow with `get_dp_value_source`, `get_bindings`, `get_applied_styles`, or `get_triggers`.

## `diagnose_visibility`

Purpose: explain why a known runtime element is or is not user-visible without relying on screenshots.

Parameters:

- `elementId` required. Obtain it from `find_elements`, `get_ui_summary`, `get_visual_tree`, or another structured response.
- `processId` optional after an active process is selected.

Output fields include `isUserVisible`, `checks`, `rootCause`, and `suggestedFix`. Typical checks cover `Visibility`, ancestor visibility, opacity, layout size, clipping, and render participation.

Example:

```json
{ "elementId": "HiddenByAncestorText_4" }
```

Recovery path: if the element is outside the active visual tree, inspect tab or virtualization state with `get_element_snapshot(elementId)` and use `get_visual_tree` only when scene tools cannot identify the active branch.

## `get_interaction_readiness`

Purpose: decide whether an element is ready for a user-like interaction before calling `click_element`, `simulate_keyboard`, or a command tool.

Parameters:

- `elementId` required.
- `processId` optional after an active process is selected.
- `interactionType` optional label, default `Click`.

Output fields include `isReady`, `blockers`, `interactionType`, `commandReadiness`, and `elementState`. `commandReadiness` reports the command source element, command name/source, `canExecute`, parameter kind, and risk notes without returning command parameter values or arbitrary ViewModel values.

Example:

```json
{ "elementId": "SaveButton_7", "interactionType": "Click" }
```

Recovery path: if `isReady` is false, address the listed blockers first. Common follow-ups are `diagnose_visibility`, `get_focus_state`, `focus_element`, `get_dp_value_source`, or `get_commands` when ViewModel inspection is explicitly allowed.

## `capture_state_snapshot`

Purpose: capture restorable runtime state before destructive testing or multi-step debugging.

Policy gate: destructive. Set `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=1` before using this tool.

Parameters:

- `processId` optional after an active process is selected.
- `elementId` optional scope.
- `propertyNames`, `viewModelPropertyNames`, and `includeFocus` select what is captured.
- `snapshotName` optional label.

Output fields include `snapshotId` and `snapshotSummary`. Snapshots are in-memory, session-scoped, and retained for a bounded time.

Example:

```json
{ "elementId": "EditorPanel", "propertyNames": ["Text"], "includeFocus": true }
```

Recovery path: keep the returned `snapshotId`; use it with `get_state_diff` after a mutation and `restore_state_snapshot` when the target should be returned to baseline.

## `batch_mutate`

Purpose: execute multiple allowed runtime mutations in order with one failure surface.

Policy gate: destructive. The server must allow destructive tools with `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=1` before this tool can run.

Parameters:

- `mutations` required array of supported mutation steps.
- `captureSnapshot` optional snapshot request.
- `includeDiff` optional; requires `captureSnapshot`.
- `trigger`, `processId`, and default `elementId` optional.

Output fields include execution counts, per-step results, optional `snapshotId`, optional `stateDiff`, and rollback guidance. Execution stops on the first failed step and does not automatically roll back.

Example:

```json
{ "captureSnapshot": { "elementId": "NameTextBox", "propertyNames": ["Text"] }, "includeDiff": true, "mutations": [{ "tool": "set_dp_value", "args": { "elementId": "NameTextBox", "propertyName": "Text", "value": "Alice" } }] }
```

Recovery path: if a step fails after earlier mutations succeeded, inspect `rollback` and call `restore_state_snapshot` when a retained snapshot is available.

## `get_state_diff`

Purpose: compare a previous state snapshot with current runtime state.

Parameters:

- `snapshotId` required from `capture_state_snapshot` or `batch_mutate`.
- `processId` optional after an active process is selected.
- `trigger` optional label describing what changed after capture.

Output fields include DependencyProperty changes, ViewModel changes, binding error deltas, validation changes, focus changes, and duration.

Example:

```json
{ "snapshotId": "snapshot_abc", "trigger": "click_element(SaveButton)" }
```

Recovery path: if the diff shows unintended changes, call `restore_state_snapshot(snapshotId)` while the snapshot is still retained.

## `restore_state_snapshot`

Purpose: restore a retained state snapshot after temporary runtime changes.

Policy gate: destructive. Set `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=1` before using this tool.

Parameters:

- `snapshotId` required.
- `processId` optional after an active process is selected.
- `removeAfterRestore` optional, default `true`.

Output fields include restored and skipped DependencyProperty values, restored and skipped ViewModel properties, focus restoration, warnings, and verification flags.

Example:

```json
{ "snapshotId": "snapshot_abc" }
```

Recovery path: if restore is incomplete, inspect skipped entries and verification fields before retrying. Reconnect and recapture if the snapshot expired or belongs to another session.
