# Glossary

Use this page when a term appears before the surrounding architecture has been explained. The short definitions here are practical; detailed contracts live in the linked reference pages.

## Core terms

| Term | Meaning |
| --- | --- |
| MCP Server | The STDIO Model Context Protocol server process that exposes WPF diagnostic tools to MCP clients. |
| MCP client | The application or agent that calls MCP tools, reads resources, and follows prompts. Treat it as untrusted from the server security model's point of view. |
| Target | The WPF executable being inspected. Security policy requires an exact local absolute executable path for every target. |
| Inspector | The in-process WPF component that runs on the target application's Dispatcher and reads WPF-native state. |
| SDK-hosted Inspector | An Inspector started by an application you own through `InspectorSdk.Initialize()`. Prefer this path for production-owned apps. |
| Raw injection | The fallback path where the server injects the Inspector into a reviewed target process. It is blocked unless explicitly allowed. |
| Bootstrapper | The native bridge that loads the correct managed Inspector runtime during raw injection. |
| Named Pipe | The local IPC channel between the MCP Server and Inspector host. Requests use length-prefixed JSON framing. |
| HMAC secret | The shared secret used for challenge-response authentication on Inspector connections. |
| TLS over named pipes | The secure transport layer used by default for Inspector communication. |

## Security terms

| Term | Meaning |
| --- | --- |
| Allowlist | A list of exact local absolute executable paths that the server may inspect or inject into. Path fragments and relative paths are not enough. |
| Fail closed | The safe default: when a policy value is missing, false, malformed, or ambiguous, the server blocks the operation. |
| Policy gate | A server-side capability check that runs before returning process details, UI text, screenshots, ViewModel values, or mutations. |
| Sensitive reads | Reads that may expose UI text, binding values, DependencyProperty values, event payloads, tree/scene summaries, or runtime state. |
| Destructive tools | Tools that can interact with or mutate the running app, including clicks, ViewModel changes, DependencyProperty changes, render measurement, snapshots, and batch mutations. |
| ViewModel inspection | Reading or using ViewModel state, commands, DataContext chains, or ViewModel mutation helpers. |
| Screenshot gate | The policy gate that must be enabled before `element_screenshot` can return pixel data or screenshot resources. |

## Response and workflow terms

| Term | Meaning |
| --- | --- |
| `structuredContent` | The canonical machine-readable WPF payload returned by modern tools. Text output is only a compact fallback. |
| `navigation.recommended` | Tool-provided next-step guidance. Prefer it before improvising a workflow. |
| `nextSteps` | Compatibility follow-up guidance for older clients. Newer clients should prefer the `navigation` envelope. |
| `prefetchTools` | Advisory tool names that a schema-aware client may load before a likely follow-up. |
| `contextRefs` | Descriptive JSON references in a response. They are not executable handles. |
| Scene summary | A compact semantic overview from `get_ui_summary`, useful before tree-heavy inspection. |
| Element snapshot | Focused one-element diagnostics from `get_element_snapshot(elementId)` after a concrete elementId is known. |
| Snapshot | Captured runtime state used to compare or restore after an approved mutation. |
| Rollback | Restoring a captured snapshot so a diagnostic mutation does not leave the app changed. |
| `diagnosticKind` | A response hint that identifies what type of diagnostic result or failure was returned. |

## Common first links

- New setup: [5-Minute Setup](../quickstart/index.md)
- Agent workflow: [AI Agent Guide](../guides/ai-agent-guide.md)
- Stable recipes: [Common Workflows](../guides/common-workflows.md)
- Runtime contracts: [MCP Contracts and Navigation](mcp-contracts.md)
- Security gates: [Security Model](../production/security.md)
- Errors: [Error Model](error-model.md)
