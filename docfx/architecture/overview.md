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

## Implementation map

| Layer | User-facing role | Main implementation area | Notes |
| --- | --- | --- | --- |
| MCP Server | Exposes tools, resources, prompts, security gates, and response navigation | `src/WpfDevTools.Mcp.Server` | This is the boundary MCP clients talk to over STDIO. |
| Inspector | Reads WPF-native runtime state and performs approved interactions inside the target process | `src/WpfDevTools.Inspector` | Runs on the target WPF Dispatcher, so it can inspect bindings, templates, and DependencyProperty precedence directly. |
| Injector | Chooses the target runtime path and coordinates raw injection fallback | `src/WpfDevTools.Injector` | Used only after target policy allows the reviewed executable path. |
| Bootstrapper | Bridges native startup into the managed Inspector runtime | `src/WpfDevTools.Bootstrapper` | Architecture matching matters here when raw injection is required. |
| SDK-hosted Inspector | Lets an app you own start the Inspector itself | `src/WpfDevTools.Inspector.Sdk` | Prefer this path for owned production apps because it avoids raw injection. |
| Shared contracts | Keeps IPC contracts, common enums, framing helpers, and security types stable | `src/WpfDevTools.Shared` | Public API docs are generated from selected shared/SDK projects only. |

## Choosing a runtime path

| Situation | Prefer | Why |
| --- | --- | --- |
| You own the WPF app source | SDK-hosted reuse | The app starts the Inspector intentionally, so raw injection does not need to be opened. |
| You need diagnostics for an unchanged local WPF app | Raw injection fallback | Useful for zero-instrumentation diagnostics, but it requires exact target allowlisting and matching bootstrapper architecture. |
| You need scene or binding context first | `connect` then `get_ui_summary` | The scene summary gives semantic context before tree-heavy inspection. |
| You need to understand wire responses | MCP contract resources | Use `wpf://contracts/tools` and `wpf://contracts/response` instead of hand-maintained assumptions. |

Related pages: [IPC and Protocol](ipc.md), [Injection and Runtime Selection](injection.md), [Security Model](../production/security.md), [Tool Reference Overview](../reference/tools/index.md), and [Glossary](../reference/glossary.md).

## Design goals

- Keep MCP contracts AI-friendly.
- Preserve low-latency local communication.
- Minimize false-positive "connected" states.
- Make runtime and architecture selection explicit.
- Keep the shipping injection path hardened by default, while requiring explicit transport coordination for SDK-host reuse.
