# Claude Desktop Setup

Claude Desktop uses a static JSON file, so the cleanest setup is to copy the generated JSON from the installed package output.

## 1. Install WPF DevTools

Preferred public path:

1. Download the matching `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Extract the package.
3. Run `setup.ps1 -Force`.

If you prefer a script-first setup, review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) and run it locally.

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
Use the WPF DevTools MCP server to connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Notes

- Start with `connect()` in the common case. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates.
- Prefer scene-level verification before visual-tree expansion.
- Keep mutation tools for later in the workflow.
- Reinstall or re-register after switching between `x64`, `x86`, and `arm64` targets.
