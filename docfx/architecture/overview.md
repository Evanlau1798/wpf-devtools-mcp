# Architecture Overview

The server uses a four-layer design:

```text
AI Client
  -> MCP over STDIO
MCP Server
  -> named pipes
Native bootstrapper + managed inspector
  -> WPF Dispatcher and in-process APIs
Target WPF application
```

## Data flow

The MCP side speaks STDIO through the official C# SDK. After `connect()` attaches or reuses a target-side host, the Inspector communicates with the MCP Server over Named Pipes using custom length-prefixed JSON request/response messages with a 4-byte little-endian length prefix.

## Why this exists

WPF inspection features such as binding introspection, dependency property precedence, and template-aware tree analysis require in-process execution. That is why the design deliberately uses an injected inspector instead of relying only on out-of-process UI automation.

When you own the target application, prefer SDK-hosted reuse; raw injection remains the fallback path for zero-instrumentation diagnostics and targets that cannot be modified.

## Main components

- **WpfDevTools.Mcp.Server/**: STDIO transport, tool routing, session management, response shaping
- **WpfDevTools.Injector/**: process validation, runtime selection, bootstrap orchestration
- **WpfDevTools.Bootstrapper/**: native bridge into the correct managed runtime
- **WpfDevTools.Inspector/**: WPF analyzers and interaction logic inside the target process
- **WpfDevTools.Inspector.Sdk/**: opt-in SDK-hosted Inspector entrypoint for target applications you own
- **WpfDevTools.Shared/**: IPC contracts, enums, common helpers, security types

## Design goals

- Keep MCP contracts AI-friendly.
- Preserve low-latency local communication.
- Minimize false-positive "connected" states.
- Make runtime and architecture selection explicit.
- Keep the shipping injection path hardened by default, while requiring explicit transport coordination for SDK-host reuse.
