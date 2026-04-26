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
- `recovery`, the canonical `recovery` object for automated recovery guidance
- `errorData`

For compatibility with older clients, the same values may also be projected into these top-level compatibility projection fields when present. Prefer `recovery.*` whenever both surfaces exist:

- `hint`
- `suggestedAction`
- `requiresReconnect`
- `processId`
- `timeoutSeconds`
- `retryAfterSeconds`
- `retryAfter`
- `availableTokens`
- `availableEvents`

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

Modern tool responses may expose a canonical `recovery` object beyond `errorCode`. Read the canonical `recovery` object first, then use top-level compatibility projection fields only as additive mirrors for older clients.

- `recovery.suggestedAction`: human-readable next step, such as retrying, reconnecting, or restarting the MCP server with elevation.
- `recovery.requiresReconnect`: indicates that the previous pipe-backed session should be treated as stale and `connect` should be called again before retrying.
- `recovery.processId`: the target process associated with a reconnect or timeout hint.
- `recovery.timeoutSeconds`: the server-side timeout budget that was exceeded.
- `recovery.retryAfterSeconds` and `recovery.retryAfter`: rate-limit recovery hints for automated backoff.

Examples:

- Timeout responses may combine `errorCode` with `recovery.requiresReconnect`, `recovery.processId`, and `recovery.timeoutSeconds` so the client can distinguish a stale pipe from a generic slow operation.
- Rate-limit responses may include `recovery.retryAfterSeconds` and `recovery.retryAfter` for deterministic retry scheduling.
- Elevation or access-denied responses may pair `errorCode` with `recovery.suggestedAction` so the next step is explicit instead of inferred from the message text.

## Agent guidance

When an action fails, ask the agent to report:

- the tool name
- the target process ID
- the exact error text
- the `recovery` object, plus any mirrored `suggestedAction`, `requiresReconnect`, or `retryAfterSeconds` compatibility fields
- the architecture involved
- whether the build was Debug or Release

That gives enough context to triage most setup failures quickly.
