# Binding and Dependency Property Tools

## Binding diagnostics

Key tools:

- `get_bindings`
- `get_binding_errors`
- `get_binding_value_chain`
- `get_datacontext_chain`
- `force_binding_update`

These are the fastest path when the UI looks wrong but the tree itself is intact.

## Dependency property analysis

Key tools:

- `get_dp_value_source`
- `get_dp_metadata`
- `set_dp_value`
- `clear_dp_value`
- `watch_dp_changes`

Use them to explain precedence, local values, styles, inheritance, triggers, and metadata.

## Mutation warning

`set_dp_value` and `clear_dp_value` mutate the live application. Follow each mutation with a verification call.