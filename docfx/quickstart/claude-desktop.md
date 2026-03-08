# Claude Desktop Setup

## Configuration file

Add the server to `claude_desktop_config.json`.

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "G:\\wpf-devtools-mcp\\src\\WpfDevTools.Mcp.Server",
        "--no-build"
      ]
    }
  }
}
```

Update the path to match your local clone.

## Recommended first prompt

```text
Use the WPF DevTools MCP server to list processes, connect to the test WPF app, and summarize the visual tree root.
```

## Claude-specific tips

- Start with `get_processes` and `connect` before asking for any per-process tool.
- Ask Claude to discover the tool schema dynamically rather than hard-coding JSON payloads.
- Prefer iterative prompts such as "inspect first, then modify" when using interaction tools.

## Safe early workflow

1. Inspect the tree.
2. Inspect bindings or dependency properties.
3. Only then call mutation tools such as `set_dp_value`, `modify_viewmodel`, or `override_style_setter`.