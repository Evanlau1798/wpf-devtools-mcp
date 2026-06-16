# Cursor, VS Code, and Visual Studio Setup

Install WPF DevTools with [5-Minute Setup](index.md), then copy the generated editor JSON for the client you use.

## Generated artifacts

```text
<InstallRoot>\<arch>\client-registration\cursor.global.json
<InstallRoot>\<arch>\client-registration\cursor.project.json
<InstallRoot>\<arch>\client-registration\vscode.json
<InstallRoot>\<arch>\client-registration\visual-studio.json
```

Cursor uses `mcpServers`:

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

VS Code and Visual Studio use `servers`:

```json
{
  "servers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

Use the generated artifacts as the source of truth instead of the sample paths.

## Verify

Set the reviewed target allowlist before starting the editor client:

```powershell
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = 'C:\Path\To\YourApp.exe'
```

First workflow:

1. `connect`
2. `get_active_process`
3. `get_ui_summary(depthMode: "semantic")`
4. a focused diagnostic from `navigation.recommended`

## Notes

- Cursor global and project registrations can coexist, but keep only one active `wpf-devtools` entry per scope.
- Cursor project registration maps to `.cursor\mcp.json`; use `cursor.project.json` as the generated source.
- Re-register after changing architecture.
- Keep editor wrappers from writing to stdout.
