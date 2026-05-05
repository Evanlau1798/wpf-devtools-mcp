# Claude Code Setup

Claude Code is the fastest public path when you want a terminal-first agent workflow with prompts and resources surfaced directly in the client.

## 1. Install Claude Code

Follow the official Claude Code installation instructions at <https://docs.claude.com/en/docs/claude-code/overview>.

If you choose to use Anthropic's PowerShell installer, download the script and review it before executing so you can audit what is being run:

```powershell
$installer = Join-Path $env:TEMP 'claude-install.ps1'
Invoke-WebRequest -Uri 'https://claude.ai/install.ps1' -OutFile $installer -UseBasicParsing
Get-Content $installer | Select-Object -First 60   # review before running
& $installer
```

> **Security note:** The `irm <url> | iex` one-liner is convenient but executes remote code without review; prefer the download-and-audit flow above, especially on untrusted networks.

## 2. Install WPF DevTools

Preferred public path:

1. Review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) as the canonical source entrypoint.
2. Run the reviewed installer locally.

Example:

```powershell
& ([scriptblock]::Create((irm https://wpf-mcptools.evanlau1798.com))) -Version latest -Client claude-code -NonInteractive -Force -OutputJson
```

Package-local fallback:

1. Download the matching `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) together with `SHA256SUMS.txt` and `release-assets.json`.
2. Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.
3. Extract the package.
4. Run `run.bat`.

Before trusting the extracted package, keep the verified release sidecars beside the archive: `SHA256SUMS.txt` for the checksum and `release-assets.json` for the canonical release metadata. If the verified archive and those sidecars are no longer adjacent to the extracted package, set `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` (or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`) before launching `run.bat` so the local install still enforces an explicit signer pin.

`run.bat` requests elevation when the current shell is not already elevated and then launches the packaged `bin/install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.

For `claude-code` and `codex`, elevated CLI registration intentionally blocks PATH-based CLI discovery. Prefer `WPFDEVTOOLS_SKIP_ELEVATION=1` for the registration step, or register manually after install. If elevated registration is unavoidable, set `WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH=1` together with a trusted absolute `WPFDEVTOOLS_CLAUDE_COMMAND_PATH` or `WPFDEVTOOLS_CODEX_COMMAND_PATH`.

If the installer cannot reuse a previous live install root and you do not pass `-InstallRoot`, the fallback executable path is:

```text
%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 3. Register the MCP server

Use the generated registration command from `client-registration\claude-code.txt`, or run the same command shape with the actual absolute executable path produced by your install:

```powershell
claude mcp add --transport stdio wpf-devtools -- "C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe"
```

Project-scoped alternative:

```powershell
claude mcp add --scope project --transport stdio wpf-devtools -- "C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe"
```

The installer also writes `client-registration\claude-code.txt`. Treat that file as the reviewed command source because it already reflects the real install root and architecture, and add `--scope project` manually when you want project-scoped setup across contributors or CI worktrees.

## 4. Verify the registration

```powershell
claude mcp list
```

## 5. First useful prompt

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 6. Discovery entry points inside Claude Code

- Prompts may appear as slash commands such as `/mcp__wpf-devtools__connect_and_list_windows`, but the portable contract is the prompt name itself.
- Resources may appear as `@wpf-devtools:capabilities` and `@wpf-devtools:limitations/elevated-targets`, but the portable contract is the resource URI.
- Use those discovery entry points when Claude Code knows the server exists but needs help selecting the right workflow.

## Notes

- Keep the server on Windows.
- Do not wrap `wpf-devtools-x64.exe` with extra stdout logging.
- Start with `connect()` in the common case. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates or when you need explicit target metadata first.
- Prefer `get_ui_summary`, `get_element_snapshot`, or `get_form_summary` before tree-heavy inspection.
- After each diagnostic, interaction, or mutation, follow `navigation.recommended` first and treat `nextSteps` as the compatibility field.
- If you already know the next tool and want a leaner payload, capable clients may pass `navigation=false` on `get_binding_errors`. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the `get_binding_errors` tool schema today, but should not assume other tools expose it yet.
- If `connect` fails, check server bitness, bootstrapper bitness, and target bitness together.
- If the target app is elevated, start Claude Code as administrator so the STDIO-launched MCP server can attach at the same integrity level.
