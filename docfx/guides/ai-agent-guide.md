# AI Agent Guide

This server is explicitly intended for AI-assisted WPF debugging and testing. The most effective agents treat the MCP tool catalog as the contract, use discovery first, and separate inspection from mutation.

## Recommended workflow

1. Discover tools and schemas.
2. Call `connect()` first and let the server auto-discover the target when there is only one visible WPF app.
3. If auto-discovery returns multiple candidates, call `get_processes(windowFilter)` and retry `connect(processId)`.
4. Use scene-level tools such as `get_ui_summary`, `get_element_snapshot`, or `get_form_summary` before falling back to tree-heavy inspection.
5. Call `ping` only when you need an explicit health check or reconnect confirmation.
6. Explore the tree to obtain stable `elementId` values only after the scene summary is insufficient.
7. Run focused diagnostics and prefer the `navigation.recommended` or `nextSteps` guidance returned by each tool.
8. Perform controlled interaction or mutation only when needed.
9. After each interaction or mutation, inspect the recommended follow-up from that response first. If the session has an active snapshot, `get_state_diff` is usually the first verification step.

## Best practices

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

- prompts such as `/mcp__wpf-devtools__debug_binding_issue`
- resources such as `@wpf-devtools:capabilities`

When present, parse these follow-up fields as part of the contract:

- `nextSteps`
- `navigation.recommended`
- `navigation.alternatives`
- `navigation.prefetchTools`
- `navigation.contextRefs`

`nextSteps` remains the compatibility field for older clients. Newer clients should prefer `navigation.recommended` and treat `alternatives` as optional human-guided branches.

### 6. Prefer scene-level aggregation before screenshots or tree dumps

The fastest agent workflows now start with one of these tools:

- `get_ui_summary` for fast semantic context
- `get_element_snapshot` for one-element triage
- `get_form_summary` for form state and submit readiness
- `get_state_diff` after an interaction or mutation

Use tree tools when exact structure matters, not as the default first step.

## Prompt patterns that work well

### Tree-first prompt

```text
Connect to the WPF test app, inspect the visual tree to find the main form controls, then summarize the top-level structure before making any changes.
Connect to the WPF test app with connect(), get_ui_summary(depthMode: "semantic"), then inspect the visual tree only if the summary is insufficient.
```

### Binding triage prompt

```text
Connect to the target WPF app, inspect binding errors, and explain which elements are failing and why. Do not modify the UI unless a fix requires it.
Connect to the target WPF app with connect(), inspect binding errors, use get_element_snapshot on the failing element, and explain which bindings are failing and why. Do not modify the UI unless a fix requires it.
```

### Safe interaction prompt

```text
Find the Save button in the current visual tree, confirm its binding and command metadata, then click it and report what changed.
Connect with connect(), get_form_summary or get_interaction_readiness for the target form, then find the Save button, confirm its command metadata, click it, and report the state diff.
```

### Snapshot-safe mutation prompt

```text
Capture a state snapshot, locate the target control, apply one UI mutation, verify the result, and restore the snapshot before finishing.
Connect with connect(), capture a state snapshot, locate the target control, apply one UI mutation, verify the result with get_state_diff, and restore the snapshot before finishing.
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

1. `connect()`
2. If needed, `get_processes(windowFilter)` and `connect(processId)`
3. `get_ui_summary` or `get_element_snapshot`
4. One or more focused diagnostics
5. One mutation or interaction at a time
6. Follow `navigation.recommended` or `nextSteps` from the latest tool result
7. If the session has an active snapshot, call `get_state_diff`
8. Use another focused verification tool only when more detail is still required

This keeps failures easy to localize and makes agent traces easier to trust.
