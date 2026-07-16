# OpenAI Codex and Codex CLI Setup

Install WPF DevTools with [5-Minute Setup](index.md), then use the generated Codex registration command.

For normal Codex setup, use the installer path from 5-Minute Setup instead of portable ZIP extraction. Portable ZIP checks are only for reviewed local archives or offline validation.

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

## Deeper gated workflows

Keep the first Codex session narrow, then enable only the gate needed for the next approved tool:

- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` for `element_screenshot`.
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` for `get_viewmodel`, command metadata, and ViewModel scopes in snapshots or `batch_mutate`.
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` for `capture_state_snapshot`, `batch_mutate`, interactions, event drain, and restore workflows.
- For confirmed `apply_ui_blueprint` and `apply_ui_project_integration` writes, set `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`, `WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true`, and an exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match before launching a fresh MCP server process. Review the [UI Composer apply and project-integration workflow](../reference/tools/ui-composer.md) first.

Before a mutation or ordered `batch_mutate`, take `capture_state_snapshot`, inspect `get_state_diff`, and restore when the workflow needs rollback.

## Troubleshooting

- Use the generated `codex.txt` command as the source of truth.
- Re-register after changing architecture or install root.
- If the target runs as administrator/elevated, launch the client from a matching elevated shell or prefer SDK-hosted diagnostics when policy blocks elevation.
- Keep wrappers from writing to stdout because the server transport is STDIO.
