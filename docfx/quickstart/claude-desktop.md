# Claude Desktop Setup

Claude Desktop uses a static JSON file, so the cleanest setup is to copy the generated JSON from the installer output.

## 1. Install WPF DevTools

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

> Security note: Review the hosted installer script before using `irm | iex` in sensitive environments.

If you do not want `irm | iex`, download the release zip manually, inspect it, and run `setup.ps1 -Force` locally before copying the generated JSON.

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
Use the WPF DevTools MCP server to connect to the running WPF app, auto-discover the target if there is only one visible candidate, and summarize the visual tree root.
```

## Notes

- Start with `connect()` in the common case. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates.
- Keep mutation tools for later in the workflow.
- Reinstall or re-register after switching between `x64`, `x86`, and `arm64` targets.
