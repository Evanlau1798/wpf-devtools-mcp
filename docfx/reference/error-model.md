# Error Model

## Layers of failure

A tool call can fail at more than one layer:

1. MCP transport or protocol layer
2. Server tool execution layer
3. Inspector response layer
4. Injection/bootstrap layer

## Fields to inspect

When available, inspect these fields in order:

- `isError` at the MCP response level
- `success` in structured content
- `error`
- `errorCode`
- `errorData`

For recovery-aware clients and agents, also inspect these additive recovery fields when present:

- `suggestedAction`
- `requiresReconnect`
- `processId`
- `timeoutSeconds`
- `retryAfterSeconds`
- `retryAfter`

## Injection-related failures

Important injection-stage outcomes include:

- `ArchitectureMismatch`
- `BootstrapFailed`
- `PipeReadyTimeout`

### Interpretation guidance

- **Architecture mismatch**: use a server/bootstrapper build that matches the target process bitness.
- **Bootstrap failed**: the bootstrapper ran but failed before the inspector was fully ready.
- **Pipe ready timeout**: the managed bridge may have been invoked, but the named pipe never became ready in time.

## Recovery contract highlights

Modern tool responses may expose structured recovery guidance beyond `errorCode`.

- `suggestedAction`: human-readable next step, such as retrying, reconnecting, or restarting the MCP server with elevation.
- `requiresReconnect`: indicates that the previous pipe-backed session should be treated as stale and `connect` should be called again before retrying.
- `processId`: the target process associated with a reconnect or timeout hint.
- `timeoutSeconds`: the server-side timeout budget that was exceeded.
- `retryAfterSeconds` and `retryAfter`: rate-limit recovery hints for automated backoff.

Examples:

- Timeout responses may combine `errorCode`, `requiresReconnect`, `processId`, and `timeoutSeconds` so the client can distinguish a stale pipe from a generic slow operation.
- Rate-limit responses may include `retryAfterSeconds` and `retryAfter` for deterministic retry scheduling.
- Elevation or access-denied responses may pair `errorCode` with `suggestedAction` so the next step is explicit instead of inferred from the message text.

## Agent guidance

When an action fails, ask the agent to report:

- the tool name
- the target process ID
- the exact error text
- any `suggestedAction`, `requiresReconnect`, or `retryAfterSeconds` fields
- the architecture involved
- whether the build was Debug or Release

That gives enough context to triage most setup failures quickly.
