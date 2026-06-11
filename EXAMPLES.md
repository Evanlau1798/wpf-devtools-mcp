# WPF DevTools MCP Server - Usage Examples

This document is the entry point for practical WPF DevTools MCP workflows. The
detailed examples are split into focused files so each document stays small and
all fenced examples remain parser-safe.

The examples follow the recommended scene-first workflow and use the modern API
where `processId` is optional after the initial connection succeeds.

## Local Prerequisites

Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to a semicolon-separated list of exact
absolute executable paths for the WPF targets you have reviewed before any
example makes an initial connection. The server fails closed when this allowlist
is missing, malformed, or does not contain the target process path.

## Quick Start

Use `connect()` first, then ask for a scene-level summary before drilling into
specific tools. The JSON below groups sequential calls as an array for parser
validation; send each request one at a time unless your client explicitly
supports batching.

```json
[
  {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "connect"
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "get_ui_summary"
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "get_element_snapshot",
      "arguments": {
        "elementId": "DataGrid_1"
      }
    }
  }
]
```

## Example Catalog

- [Scene inspection and diagnostics](examples/scene-inspection.md): scene-first
  inspection, binding errors, MVVM inspection, and DependencyProperty value
  source investigation.
- [State and interaction workflows](examples/state-and-interaction.md):
  snapshot-safe mutation, UI interaction, validation checks, and property change
  waiting.
- [Layout, process selection, and agent tips](examples/layout-process-and-agent-tips.md):
  visibility triage, process disambiguation, token efficiency, and recovery
  patterns.

## Parser Safety

- Fenced `json` blocks are valid JSON and avoid comments, trailing commas, and
  multiple top-level values.
- Multi-step JSON-RPC examples are grouped as arrays for documentation parsing.
- Shell and PowerShell snippets, when present, should omit prompt markers and
  placeholder ellipses.
