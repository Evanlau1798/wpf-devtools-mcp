# AI Agent Guide

This server is explicitly intended for AI-assisted WPF debugging and testing. The most effective agents treat the MCP tool catalog as the contract, use discovery first, and separate inspection from mutation.

## Recommended workflow

1. Discover tools and schemas.
2. Call `get_processes`.
3. Call `connect(processId)`.
4. Call `ping`.
5. Explore the tree to obtain stable `elementId` values.
6. Run diagnostics.
7. Perform controlled interaction or mutation only when needed.

## Best practices

### 1. Discover before assuming

Do not hard-code argument shapes from stale prompts or screenshots. Use the tool metadata exposed by the server and adapt to the current schema.

### 2. Treat `elementId` as runtime state

`elementId` values are session-specific runtime identifiers. Always fetch them from the current tree instead of caching them across runs.

### 3. Distinguish inspection from mutation

Inspection tools are typically safe to call repeatedly. Mutation tools change the running UI and should be used with clear intent.

Examples of mutation tools:

- `set_dp_value`
- `clear_dp_value`
- `modify_viewmodel`
- `override_style_setter`
- `click_element`
- `simulate_keyboard`
- `drag_and_drop`

### 4. Respect tool semantics

Some tools sound similar but have different intent:

- `click_element` simulates a logical button click and is the right choice for invoking button behavior.
- `fire_routed_event` raises an event route; it is not a replacement for an input gesture.
- `simulate_keyboard` is best when keyboard focus matters.
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

## Prompt patterns that work well

### Tree-first prompt

```text
Connect to the WPF test app, inspect the visual tree to find the main form controls, then summarize the top-level structure before making any changes.
```

### Binding triage prompt

```text
Connect to the target WPF app, inspect binding errors, and explain which elements are failing and why. Do not modify the UI unless a fix requires it.
```

### Safe interaction prompt

```text
Find the Save button in the current visual tree, confirm its binding and command metadata, then click it and report what changed.
```

## Anti-patterns

- Reusing old `elementId` values from a previous run.
- Calling mutation tools before confirming the target element.
- Treating `fire_routed_event` as a guaranteed substitute for user input.
- Assuming the target process is x64 without checking `get_processes`.
- Ignoring architecture or bootstrapper requirements when `connect` fails.

## Golden sequence for automation

For end-to-end automated validation, use this order whenever possible:

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`
5. One or more diagnostics
6. One mutation or interaction at a time
7. A verification tool call after each mutation

This keeps failures easy to localize and makes agent traces easier to trust.