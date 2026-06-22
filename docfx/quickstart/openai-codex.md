# OpenAI Codex and Codex CLI Setup

Install WPF DevTools with [5-Minute Setup](index.md), then use the generated Codex registration command.

## Register

The installer writes:

```text
<InstallRoot>\<arch>\client-registration\codex.txt
```

Run the command shown in that file from the shell where Codex CLI is available. It should point to:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## Verify

Set the reviewed target path and the scene-summary read gate before launching the client:

```powershell
$target = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

Then ask Codex:

```text
Use the WPF DevTools MCP server. Connect to the allowlisted WPF target and return get_ui_summary(depthMode: "semantic").
```

## Troubleshooting

- Use the generated `codex.txt` command as the source of truth.
- Re-register after changing architecture or install root.
- If the target runs as administrator/elevated, launch the client from a matching elevated shell or prefer SDK-hosted diagnostics when policy blocks elevation.
- Keep wrappers from writing to stdout because the server transport is STDIO.
