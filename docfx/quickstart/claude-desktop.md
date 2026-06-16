# Claude Desktop Setup

Install WPF DevTools with [5-Minute Setup](index.md), then copy the generated Claude Desktop JSON into your local Claude Desktop configuration.

## Register

The installer writes:

```text
<InstallRoot>\<arch>\client-registration\claude-desktop.json
```

The structure is:

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

Copy the generated artifact rather than the sample path above.

## Verify

Set the reviewed target allowlist before launching Claude Desktop:

```powershell
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = 'C:\Path\To\YourApp.exe'
```

First prompt:

```text
Use the WPF DevTools MCP server. Connect to the allowlisted WPF target, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Troubleshooting

- Restart Claude Desktop after changing its MCP configuration.
- Re-copy the generated JSON after changing install root or architecture.
- Keep mutation tools disabled until the workflow explicitly requires them.
