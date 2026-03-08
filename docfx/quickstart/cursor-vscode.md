# Cursor and VS Code Setup

## Path rule used in this guide

VS Code and Cursor MCP settings are usually stored as JSON, so use an absolute project path instead of a machine-specific sample path.

Replace `<ABSOLUTE_PATH_TO_REPO>` with your local clone path on any drive, for example `D:\dev\wpf-devtools-mcp` or `E:\repos\wpf-devtools-mcp`.

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
        "<ABSOLUTE_PATH_TO_REPO>\\src\\WpfDevTools.Mcp.Server",
        "--no-build"
      ]
    }
  }
}
```

If you want to resolve the absolute path from the repository root first, run:

```powershell
Resolve-Path .\src\WpfDevTools.Mcp.Server
```

Then paste the resolved path into the JSON value above.

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
