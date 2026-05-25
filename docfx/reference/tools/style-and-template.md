# Style And Template Tools

Use these tools when the visible WPF result depends on Style, Template, Trigger, or ResourceDictionary behavior. They require an active connection; call `connect()` first or pass a connected `processId`.

## `get_applied_styles`

Purpose: inspect the Style objects that currently apply to an element and explain where visual values come from.

Parameters:

- `processId` optional after an active process is selected.
- `elementId` optional. Omit to inspect the root window.
- `elementIds` optional batch mode. Use either `elementId` or `elementIds`.
- `compact` optional. Use `true` for style summaries without full setter payloads.

Output fields include `hasStyle`, `styles`, `setters`, `localResourceReferenceCount`, and `localResourceReferences`. This tool is read-only and does not change runtime state.

Example:

```json
{ "elementId": "SaveButton", "compact": true }
```

Recovery path: if the element is not found, refresh candidates with `find_elements` or use `get_element_snapshot` after a known `elementId`.

## `get_triggers`

Purpose: inspect style and template triggers that may conditionally set properties.

Parameters:

- `elementId` required.
- `processId` optional after an active process is selected.

Output fields include `triggers`, `triggerType`, `conditions`, and `setters`. The result is diagnostic only; it does not invoke event triggers or mutate the target.

Example:

```json
{ "elementId": "SaveButton" }
```

Recovery path: if no trigger explains the visible value, follow with `get_dp_value_source` for the specific DependencyProperty.

## `get_resource_chain`

Purpose: trace ResourceDictionary lookup for a runtime resource key from element scope outward.

Parameters:

- `resourceKey` required.
- `processId` optional after an active process is selected.
- `elementId` optional starting point. Omit to start at the root window.

Output fields include `found`, `chain`, `level`, `dictionarySource`, and `value`. This is read-only and can reveal UI text or resource values from the target, so use it only on reviewed allowlisted targets.

Example:

```json
{ "elementId": "SaveButton", "resourceKey": "PrimaryBrush" }
```

Recovery path: if `found` is false, inspect the relevant element scope with `get_namescope` or check the root with `get_resource_chain` without `elementId`.

## `override_style_setter`

Purpose: test a runtime-only style value by applying a local value that takes precedence over the style.

Policy gate: destructive. The server must allow destructive tools through `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS`; otherwise the call fails closed. Use `capture_state_snapshot` before this tool and `restore_state_snapshot` after validation when the app should be left unchanged.

Parameters:

- `elementId` required.
- `propertyName` required.
- `value` required JSON value.
- `processId` optional after an active process is selected.
- `detail` optional: `compact`, `minimal`, `verbose`, or `standard`.

Output fields include `success`, `propertyName`, `oldValue`, `newValue`, `valueType`, and optional mutation metadata. The change is not persisted to XAML.

Example:

```json
{ "elementId": "SaveButton", "propertyName": "Background", "value": "Red", "detail": "verbose" }
```

Recovery path: if conversion fails, inspect the target property with `get_dp_metadata` and verify the effective result with `get_dp_value_source` or `get_state_diff`.
