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

## Injection-related failures

Important injection-stage outcomes include:

- `ArchitectureMismatch`
- `BootstrapFailed`
- `PipeReadyTimeout`

### Interpretation guidance

- **Architecture mismatch**: use a server/bootstrapper build that matches the target process bitness.
- **Bootstrap failed**: the bootstrapper ran but failed before the inspector was fully ready.
- **Pipe ready timeout**: the managed bridge may have been invoked, but the named pipe never became ready in time.

## Agent guidance

When an action fails, ask the agent to report:

- the tool name
- the target process ID
- the exact error text
- the architecture involved
- whether the build was Debug or Release

That gives enough context to triage most setup failures quickly.