# Claude Code Setup

Claude Code is the terminal-first setup path. Install WPF DevTools with [5-Minute Setup](index.md), then use the generated Claude Code registration command.

## Register

The installer writes:

```text
<InstallRoot>\<arch>\client-registration\claude-code.txt
```

Run the command shown in that file from the shell where Claude Code is available. It should register the installed package executable, not a source-tree command.

Fallback executable shape:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## Verify

Before starting Claude Code, set the reviewed target allowlist:

```powershell
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = 'C:\Path\To\YourApp.exe'
```

Then ask Claude Code:

```text
Use the WPF DevTools MCP server. Connect to the allowlisted WPF target, then summarize the current UI with get_ui_summary(depthMode: "semantic").
```

## Troubleshooting

- Re-run the generated registration command after switching install roots or architectures.
- Claude Code can expose slash-command style MCP entry points such as `/mcp__wpf-devtools__...` and resource mentions such as `@wpf-devtools:`; keep the generated `claude-code.txt` command as the registration source.
- If Claude Code cannot launch the server, verify the command path points to `wpf-devtools-<arch>.exe` under the installed package.
- If `connect` is denied, fix `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` before enabling any other gate.
