# AI Agent Guide

This server is explicitly intended for AI-assisted WPF debugging and testing. The most effective agents treat the MCP tool catalog as the contract, use discovery first, and separate inspection from mutation.

## Recommended workflow

1. Discover tools and schemas.
2. Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` includes the reviewed target's exact absolute executable path; unset or malformed values fail closed before `connect` attaches.
3. Call `connect()` first and let the server auto-discover the target when there is only one visible WPF app.
4. If auto-discovery returns multiple candidates, call `get_processes(windowFilter)` and retry `connect(processId)`.
5. Use directly executable scene-level tools such as `get_ui_summary` or `get_form_summary` before falling back to tree-heavy inspection.
6. Explore the tree or use focused search to obtain stable `elementId` values; call `get_element_snapshot(elementId)` only after a concrete elementId is known.
7. Run focused diagnostics and prefer the `navigation.recommended` or `nextSteps` guidance returned by each tool.
8. Perform controlled interaction or mutation only when needed.
9. After each interaction or mutation, inspect the recommended follow-up from that response first. If the session has an active snapshot, `get_state_diff` is usually the first verification step.
10. Call `ping` only when you need an explicit health check or reconnect confirmation.

## Best practices

### 0. Keep the server instructions AI-friendly

Follow the same authoring rules that the official MCP and Anthropic guidance emphasize:

- Detailed `tool descriptions` should explain what a tool does, `when to use` it, `when not to use` it, and any important limits or caveats.
- JSON schema and SDK annotations help discovery, but they are not `runtime validation`; tool handlers still need to validate untrusted arguments explicitly at runtime.
- Prefer realistic client workflows, prompts, and resources over raw protocol walkthroughs when writing public quickstarts.

### 1. Discover before assuming

Do not hard-code argument shapes from stale prompts or screenshots. Use the tool metadata exposed by the server and adapt to the current schema.

### 2. Treat `elementId` as runtime state

`elementId` values are session-specific runtime identifiers. Always fetch them from the current tree instead of caching them across runs.

### 3. Distinguish inspection from mutation

Inspection tools are typically safe to call repeatedly. Mutation tools change the running UI and should be used with clear intent.

For stateful validation, prefer this sequence:

1. `capture_state_snapshot`
2. Inspect and mutate once
3. Verify by following `navigation.recommended` or `nextSteps`
4. If the snapshot is still active, call `get_state_diff`
5. `restore_state_snapshot` if the app should be left unchanged

Local policy gates must be confirmed close to any prompt or example that uses
high-risk tools. `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` must include the target
executable's exact absolute path before `connect`. In addition,
`WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` gates mutation tools such as
`click_element`, `set_dp_value`, `restore_state_snapshot`, and `batch_mutate`;
`WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` gates `element_screenshot`; and
`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` gates `get_viewmodel` and
`get_commands`. `execute_command` requires both
`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` and
`WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS`.

Examples of mutation tools:

- `set_dp_value`
- `clear_dp_value`
- `modify_viewmodel`
- `override_style_setter`
- `click_element`
- `simulate_keyboard`
- `drag_and_drop`
- `focus_element`

### 4. Respect tool semantics

Some tools sound similar but have different intent:

- `click_element` simulates a logical button click and is the right choice for invoking button behavior.
- `fire_routed_event` raises an event route; it is not a replacement for an input gesture.
- `simulate_keyboard` is best when keyboard focus matters, and `get_focus_state` should usually be checked first.
- `drag_and_drop` is intended for controlled payload transfer and currently works best with explicit text payload scenarios.

### 5. Parse structured results carefully

Tool calls can fail at multiple layers:

- MCP protocol layer
- Tool execution layer
- Inspector response layer
- Injection/bootstrap layer

Check these fields when present:

- `success`
- `error`
- `errorCode`
- `errorData`
- `diagnosticKind`
- `sourceKind`

Also use MCP discovery surfaces instead of relying on memory:

- prompts such as `debug_binding_issue`
- resources such as `wpf://capabilities`

Some clients may render these as client-specific shortcuts such as `/mcp__wpf-devtools__debug_binding_issue` or `@wpf-devtools:capabilities`, but the standard prompt name and resource URI are the portable contract.

When present, parse these follow-up fields as part of the contract:

- `nextSteps`
- `navigation.recommended`
- `navigation.alternatives`
- `navigation.prefetchTools`
- `navigation.contextRefs`

`nextSteps` remains the compatibility field for older clients. Newer clients should prefer `navigation.recommended` and treat `alternatives` as optional human-guided branches.

If the next action is already obvious, capable clients may pass `navigation=false` on `get_binding_errors` to omit `nextSteps` and `navigation` from that response and save tokens. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the `get_binding_errors` tool schema today, and should not assume other tools accept it unless their schema advertises it too.

### 6. Prefer scene-level aggregation before screenshots or tree dumps

The fastest agent workflows now start with one of these tools:

- `get_ui_summary` for fast semantic context
- `get_element_snapshot(elementId)` for one-element triage after a concrete elementId is known
- `get_form_summary` for form state and submit readiness
- `get_state_diff` after an interaction or mutation

Use tree tools when exact structure matters, not as the default first step.

### 7. Prefer compact diagnostics unless verbose detail is required

- `get_binding_errors` defaults to `compact=true`; keep that default unless you explicitly need full per-error message text.
- Use `get_affected_elements` before broad recursive binding inspection when you already know the suspect binding path or property.
- Use `drain_events` when you need an explicit read of buffered `BindingError`, `DpChange`, or validation events instead of relying on opportunistic piggyback fields.

### 8. Use sequential mutation orchestration deliberately

When a workflow needs multiple ordered live mutations, prefer `batch_mutate` over improvising several destructive calls in one reasoning step. It keeps the sequence explicit, preserves per-operation results, and is easier to verify with `get_state_diff`, `drain_events`, or focused follow-up tools.

## Prompt patterns that work well

### Scene-first prompt

```text
Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the WPF test app's exact absolute executable path; unset or malformed values fail closed before connect() attaches. Then connect with connect(), get_ui_summary(depthMode: "semantic"), and inspect the visual tree only if the summary is insufficient.
```

### Binding triage prompt

```text
Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the target WPF app's exact absolute executable path; unset or malformed values fail closed before connect() attaches. Then connect with connect(), inspect binding errors with compact defaults, use get_affected_elements or get_element_snapshot(elementId) after identifying a concrete failing element, and explain which bindings are failing and why. Do not modify the UI unless a fix requires it.
```

### Safe interaction prompt

```text
Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the target WPF app's exact absolute executable path and WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true is set before clicking; unset or malformed values fail closed before connect() attaches. Then connect with connect(), get_form_summary or get_interaction_readiness for the target form, find the Save button, confirm its command metadata, click it, drain buffered runtime events if present, and report the state diff.
```

### Snapshot-safe mutation prompt

```text
Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the target WPF app's exact absolute executable path and WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true is set before mutation; unset or malformed values fail closed before connect() attaches. Then connect with connect(), capture a state snapshot, locate the target control, apply one UI mutation or an ordered batch_mutate sequence, verify the result with get_state_diff, and restore the snapshot before finishing.
```

## Anti-patterns

- Reusing old `elementId` values from a previous run.
- Calling mutation tools before confirming the target element.
- Skipping `capture_state_snapshot` before a mutation that may need rollback.
- Treating `fire_routed_event` as a guaranteed substitute for user input.
- Assuming the target process is x64 without checking `get_processes(windowFilter)` when auto-discovery is ambiguous.
- Ignoring architecture or bootstrapper requirements when `connect` fails.

## Golden sequence for automation

For end-to-end automated validation, use this order whenever possible:

1. Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` contains the target's exact absolute executable path; unset or malformed values fail closed before `connect()` attaches
2. `connect()`
3. If needed, `get_processes(windowFilter)` and `connect(processId)`
4. `get_ui_summary` or `get_form_summary`
5. One or more focused diagnostics; use `get_element_snapshot(elementId)` only after a concrete elementId is known
6. One mutation or interaction at a time
7. Follow `navigation.recommended` or `nextSteps` from the latest tool result
8. If the session has an active snapshot, call `get_state_diff`
9. If the session has buffered runtime events, call `drain_events`
10. Use another focused verification tool only when more detail is still required

This keeps failures easy to localize and makes agent traces easier to trust.
