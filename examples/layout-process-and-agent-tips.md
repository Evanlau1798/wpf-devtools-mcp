# Layout, Process Selection, and Agent Tips

These examples cover visibility triage, process disambiguation, and practical
agent guidance. The JSON blocks group sequential calls as arrays for parser
validation; send each request one at a time unless your client explicitly
supports batching.

## Local Prerequisites

Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` contains the target executable's exact
absolute path before `connect()`. The server fails closed when this allowlist is
missing, malformed, or does not contain the target process path.

## Example 8: Layout Debugging

### Scenario

An element is clipped or not visible, and you want to understand why.

### Solution

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
      "name": "diagnose_visibility",
      "arguments": {
        "elementId": "StackPanel_2"
      }
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "get_layout_info",
      "arguments": {
        "elementId": "StackPanel_2"
      }
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 4,
    "method": "tools/call",
    "params": {
      "name": "get_clipping_info",
      "arguments": {
        "elementId": "StackPanel_2"
      }
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 5,
    "method": "tools/call",
    "params": {
      "name": "highlight_element",
      "arguments": {
        "elementId": "StackPanel_2",
        "duration": 5000
      }
    }
  }
]
```

## Example 9: Process Disambiguation

### Scenario

Multiple WPF applications are running and you need to connect to a specific one.

### Solution

```json
[
  {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "get_processes",
      "arguments": {
        "nameFilter": "MyApp"
      }
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "connect",
      "arguments": {
        "processId": 12345
      }
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "get_ui_summary"
    }
  }
]
```

## Tips for AI Agents

### Scene-First Approach

Always start with `get_ui_summary` after `connect()` to understand the UI
structure before diving into specific tools. Follow `navigation.recommended`
hints in responses.

### Efficient Tree Traversal

Use the `depth` parameter to minimize token usage:

```json
{
  "name": "get_visual_tree",
  "arguments": {
    "depth": 3
  }
}
```

### Token Efficiency

- Prefer scene-level tools such as `get_ui_summary`, `get_element_snapshot`, and
  `get_form_summary` over tree dumps.
- For screenshots, use `outputMode: "metadata"` or `outputMode: "file"` instead
  of base64.
- Use `compact: true` where available to reduce response size.

### Filtering Processes

Use `nameFilter` to narrow by process name. Use `windowFilter` only with
`visible`, `all`, or `foreground`.

```json
{
  "name": "get_processes",
  "arguments": {
    "nameFilter": "MyApp"
  }
}
```

### Connection Health

Use `ping` only when an explicit health check is needed, not as a polling
mechanism.

```json
{
  "name": "ping"
}
```

### Error Recovery

Responses include structured error codes and hints. Common patterns:

- `NotConnected`: call `connect()` first.
- `ElementNotFound`: use `find_elements` to search by type or name.
- `Timeout`: the target app may be busy; retry with a larger timeout.
