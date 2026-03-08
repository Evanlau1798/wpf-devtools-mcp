# Claude Desktop Setup

Claude Desktop uses a static JSON file, so the cleanest setup is to copy the generated JSON from the installer output.

## 1. Install WPF DevTools

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

After installation, the default executable path is:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 2. Generated JSON template

The installer writes `client-registration\claude-desktop.json`. Its structure is:

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "command": "%LOCALAPPDATA%\\WpfDevToolsMcp\\x64\\current\\WpfDevTools.Mcp.Server.exe",
      "args": []
    }
  }
}
```

Copy the installed path into your local `claude_desktop_config.json` if you need to adapt the architecture.

## 3. First prompt

```text
Use the WPF DevTools MCP server to list processes, connect to the target app, and summarize the visual tree root.
```

## Notes

- Start with `get_processes` and `connect`.
- Keep mutation tools for later in the workflow.
- Reinstall or re-register after switching between `x64`, `x86`, and `arm64` targets.
