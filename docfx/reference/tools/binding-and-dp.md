# Binding and Dependency Property Tools

## Binding diagnostics

Key tools:

- `get_affected_elements`
- `get_bindings`
- `get_binding_errors`
- `get_binding_mismatches`
- `get_binding_value_chain`
- `get_datacontext_chain`
- `force_binding_update`

These are the fastest path when the UI looks wrong but the tree itself is intact.

Use `get_affected_elements` first when you already know a binding path or `DataContext` property that may be stale and want a cheap candidate scan before broad recursive binding inspection.

`get_binding_errors` defaults to `compact=true`, which trims verbose per-error message text from the main `errors` array. Pass `compact=false` only when you need the full message payload for manual debugging.

When the next action is already obvious, capable clients may pass `navigation=false` to `get_binding_errors` to omit the compatibility `nextSteps` and `navigation` payload from that specific response. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the `get_binding_errors` tool schema today, and should not assume other diagnostic tools expose it yet.

Use `get_binding_mismatches` when the binding path resolves but the value still looks wrong because of type mismatches, nullability mismatches, or converter interactions.

`get_affected_elements` is intentionally conservative:

- elements driven by `ElementName`, `RelativeSource`, or explicit `Source`
- elements with no usable `DataContext` chain
- elements that cannot be proven to still depend on the requested path

These are returned through `unsupportedElements` with an `unsupportedReason` instead of being mixed into the supported candidate set.

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

For serialized STDIO clients that need to mutate and then wait inside one bounded request, prefer `wait_for_dp_change(triggerMutation=...)` over improvising a manual polling loop. Treat that form as a destructive workflow because the server executes the supplied mutation before waiting.

If `triggerMutation` itself exhausts the remaining timeout budget, `wait_for_dp_change` returns `completionReason: "TriggerMutationTimedOut"`, marks `stateAfterTimeoutUnknown: true`, and sets `requiresReconnect: true`. That means the server reset the pipe to avoid a stale in-flight response; reconnect and re-read state before assuming whether the mutation eventually landed.

Use `drain_events` when you need a deterministic explicit read of buffered `DpChange`, `BindingError`, or validation events after a mutation, interaction, or watcher registration.

## Mutation warning

`set_dp_value` and `clear_dp_value` mutate the live application. Follow each mutation with a verification call such as `get_state_diff`, `get_dp_value_source`, or `drain_events`.
