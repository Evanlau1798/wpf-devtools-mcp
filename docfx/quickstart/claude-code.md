# Claude Code Setup

Claude Code is the fastest public path when you want a terminal-first agent workflow with prompts and resources surfaced directly in the client.

## 1. Install Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
```

## 2. Install WPF DevTools

Preferred public path:

1. Download the matching `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Extract the package.
3. Run `run.bat`.

If you prefer a script-first setup, review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) and run it locally.

Example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -Force
```

After installation, the default executable path is:

```text
%APPDATA%\WpfDevToolsMcp\x64\current\bin\wpf-devtools-x64.exe
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

- Prompts appear as slash commands such as `/mcp__wpf-devtools__connect_and_list_windows`.
- Resources appear as `@wpf-devtools:capabilities` and `@wpf-devtools:limitations/elevated-targets`.
- Use those discovery entry points when Claude Code knows the server exists but needs help selecting the right workflow.

## Notes

- Keep the server on Windows.
- Do not wrap `wpf-devtools-x64.exe` with extra stdout logging.
- Start with `connect()` in the common case. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates or when you need explicit target metadata first.
- Prefer `get_ui_summary`, `get_element_snapshot`, or `get_form_summary` before tree-heavy inspection.
- If you already know the next tool and want a leaner payload, pass `navigation=false` on that specific call.
- If `connect` fails, check server bitness, bootstrapper bitness, and target bitness together.
- If the target app is elevated, start Claude Code as administrator so the STDIO-launched MCP server can attach at the same integrity level.
