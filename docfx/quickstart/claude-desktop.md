# Claude Desktop Setup

## Path rule used in this guide

Claude Desktop stores a static JSON configuration file, so the server entry should use an absolute project path.

Replace `<ABSOLUTE_PATH_TO_REPO>` with your local clone path on any drive, for example `D:\dev\wpf-devtools-mcp` or `E:\repos\wpf-devtools-mcp`.

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
        "<ABSOLUTE_PATH_TO_REPO>\\src\\WpfDevTools.Mcp.Server",
        "--no-build"
      ]
    }
  }
}
```

If you are currently in the repository root and want to resolve the path first, run:

```powershell
Resolve-Path .\src\WpfDevTools.Mcp.Server
```

Copy the resolved absolute path into the JSON value above.

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
