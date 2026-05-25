# ADR-006: STDIO single-session boundary

## Status

Accepted.

## Context

The shipping MCP server is a local STDIO server launched by an MCP client as a child process. The official MCP and C# SDK documentation describe STDIO as the local child-process integration model, while Streamable HTTP and legacy SSE are separate HTTP transports for remote or service-style deployments.

The current implementation is therefore allowed to keep some state at process or host scope:

- `ToolCallHelper` owns shared tool wrappers, response shaping helpers, navigation planner defaults, and host-keyed tool caches.
- `MetricsCollector` stores process-wide method metrics for the running server process.
- `SessionManager` owns active target process state, process sessions, cleanup timers, and rate-limit state for the current server process.
- `SessionNavigationStateStore` tracks per-target snapshot and active trace navigation state behind `SessionManager`.
- `ToolNavigationPlanner` and the registered navigation catalog are shared guidance for all tool calls in this server process.
- `McpToolExecutionPolicy` evaluates the current policy profile for destructive tools, screenshots, and ViewModel inspection.

That model is acceptable only while one STDIO server process serves one MCP client session. HTTP transports can multiplex multiple clients, requests, or resumable event streams through the same process. Reusing these globals in that model could leak active process selection, metrics, navigation context, cached tool instances, or policy decisions across clients.

## Decision

Keep the public server on STDIO single-session semantics until HTTP transport work explicitly replaces or scopes the state listed above.

No Streamable HTTP or SSE server endpoint may be released until a release gate verifies that the following are moved into DI/request/session scope:

- tool instance caches and any `ToolCallHelper` override state
- method metrics and rate-limit counters
- active process/session state from `SessionManager`
- snapshot, active trace, and navigation state from `SessionNavigationStateStore`
- `ToolNavigationPlanner` request context and navigation catalog access
- policy profile evaluation currently centralized in `McpToolExecutionPolicy`

The gate must include tests that run two independent logical clients in one server process and prove that process selection, policy settings, navigation state, metrics, and cached tool instances do not cross client boundaries.

## Consequences

- Current static state remains acceptable for the shipping STDIO package.
- Future HTTP work must start with session-bound service registration instead of only swapping transports.
- Any future `MapMcp`, Streamable HTTP, or SSE entrypoint must fail review if it keeps using STDIO-only process globals.
- Documentation, smoke tests, and production readiness reviews must treat session isolation as a release gate, not a cleanup task after transport launch.

## References

- MCP server guide: https://modelcontextprotocol.io/docs/develop/build-server
- MCP C# SDK transports: https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html
