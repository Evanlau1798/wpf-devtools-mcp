# Scene And State Diagnostic Tools

Use these tools after `connect()` to get semantic state before expanding large trees or taking screenshots. They are read-only unless noted elsewhere for state capture, mutation, or restore workflows.

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

Recovery path: if the element is outside the active visual tree, inspect tab or virtualization state with `get_element_snapshot(elementId)` and use `get_visual_tree` only when the scene tools cannot identify the active branch.

## `get_interaction_readiness`

Purpose: decide whether an element is ready for a user-like interaction before calling `click_element`, `simulate_keyboard`, or a command tool.

Parameters:

- `elementId` required.
- `processId` optional after an active process is selected.
- `interactionType` optional label, default `Click`.

Output fields include `isReady`, `blockers`, `interactionType`, and `elementState`. The tool can surface command readiness without exposing arbitrary ViewModel values.

Example:

```json
{ "elementId": "SaveButton_7", "interactionType": "Click" }
```

Recovery path: if `isReady` is false, address the listed blockers first. Common follow-ups are `diagnose_visibility`, `get_focus_state`, `focus_element`, `get_dp_value_source`, or `get_commands` when ViewModel inspection is explicitly allowed.
