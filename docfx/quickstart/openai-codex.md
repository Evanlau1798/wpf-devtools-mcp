# OpenAI Codex and Codex CLI Setup

Use this guide when you want the installed WPF DevTools server to be available from Codex workflows.

## 1. Install Codex CLI

```powershell
npm install -g @openai/codex
```

## 2. Install WPF DevTools

Preferred public path:

1. Download the matching `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Extract the package.
3. Run `run.bat`.

If you prefer a script-first setup, review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) and run it locally.

Example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client codex -NonInteractive -Force -OutputJson
```

If the installer cannot reuse a previous live install root and you do not pass `-InstallRoot`, the fallback executable path is:

```text
%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 3. Register the MCP server

Use the generated command from `client-registration\codex.txt`, or run the same command shape with the actual absolute executable path produced by your install:

```powershell
codex mcp add wpf-devtools -- "C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe"
```

## 4. Verify the registration

```powershell
codex mcp list
```

## 5. First useful prompt

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Notes

- Keep the MCP server on Windows even if your editor tooling spans multiple environments.
- Start with `connect()` in the common case. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates or when you want explicit target metadata first.
- Prefer scene-level tools before tree-heavy inspection.
- After each diagnostic, interaction, or mutation, follow `navigation.recommended` first and use `nextSteps` only as the compatibility field.
- If you already know the next tool and want a leaner payload, capable clients may pass `navigation=false` on `get_binding_errors`, but schema-driven clients should only rely on that opt-out when their MCP stack can send optional args beyond the advertised tool schema and should not assume other tools expose it yet.
- If `connect` fails, check server bitness, bootstrapper bitness, and the target process bitness together.
- Keep `stdout` clean because Codex uses STDIO MCP transport.
- If the target app is elevated, start Codex or the host terminal as administrator. A non-administrator Codex host can usually discover the process but cannot control an elevated target.
