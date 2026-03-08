# Cursor and VS Code Setup

## VS Code MCP configuration

Create or update `.vscode/mcp.json`:

```json
{
  "servers": {
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

## Cursor configuration

Use the same command/args pair in Cursor's MCP server settings.

## Recommended workspace practice

- Keep the WPF application and the MCP server in separate terminals.
- Rebuild the server if you change analyzer or tool code.
- Keep generated logs off `stdout`; MCP transport depends on a clean STDIO channel.

## First useful actions

- Ask the editor agent to call `tools/list`.
- Use `get_processes` to find the target PID.
- Use `connect` before any process-specific workflow.