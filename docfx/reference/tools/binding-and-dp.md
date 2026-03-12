# Binding and Dependency Property Tools

## Binding diagnostics

Key tools:

- `get_bindings`
- `get_binding_errors`
- `get_binding_mismatches`
- `get_binding_value_chain`
- `get_datacontext_chain`
- `force_binding_update`

These are the fastest path when the UI looks wrong but the tree itself is intact.

Use `get_binding_mismatches` when the binding path resolves but the value still looks wrong because of type mismatches, nullability mismatches, or converter interactions.

## Dependency property analysis

Key tools:

- `get_dp_value_source`
- `get_dp_metadata`
- `set_dp_value`
- `clear_dp_value`
- `watch_dp_changes`
- `wait_for_dp_change`

Use them to explain precedence, local values, styles, inheritance, triggers, and metadata.

Use `wait_for_dp_change` when you need a polling-friendly timeout-bounded wait over STDIO. It is the recommended fallback when `watch_dp_changes` can only register interest but cannot push live events.

## Mutation warning

`set_dp_value` and `clear_dp_value` mutate the live application. Follow each mutation with a verification call.
