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

## Why this exists

WPF inspection features such as binding introspection, dependency property precedence, and template-aware tree analysis require in-process execution. That is why the design deliberately uses an injected inspector instead of relying only on out-of-process UI automation.

## Main components

- **MCP Server**: tool routing, session management, response shaping
- **Injector**: process validation, runtime selection, bootstrap orchestration
- **Bootstrapper**: native bridge into the correct managed runtime
- **Inspector**: WPF analyzers and interaction logic inside the target process
- **Shared**: IPC contracts, enums, common helpers, security types

## Design goals

- Keep MCP contracts AI-friendly.
- Preserve low-latency local communication.
- Minimize false-positive "connected" states.
- Make runtime and architecture selection explicit.
- Keep production hardening opt-in but real.
