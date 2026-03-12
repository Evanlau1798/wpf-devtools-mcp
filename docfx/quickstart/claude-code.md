# Claude Code Setup

Claude Code is the fastest public path when you want a terminal-first agent workflow.

## 1. Install Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
```

## 2. Install WPF DevTools

Fastest path:

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

One-command Claude Code focused setup:

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code -NonInteractive -Force
```

After installation, the default executable path is:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 3. Register the MCP server

Use the generated registration command from `client-registration\claude-code.txt`, or run:

```powershell
claude mcp add --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

Project-scoped alternative:

```powershell
claude mcp add --scope project --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

The installer also writes `client-registration\claude-code.project.mcp.json`. Use that artifact when you want reproducible project-scoped setup across contributors or CI worktrees.

## 4. Verify the registration

```powershell
claude mcp list
```

## 5. First useful prompt

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, and summarize the root UI state.
```

## 6. Discovery entry points inside Claude Code

- Prompts appear as slash commands such as `/mcp__wpf-devtools__connect_and_list_windows`.
- Resources appear as `@wpf-devtools:capabilities` and `@wpf-devtools:limitations/elevated-targets`.
- Use these discovery entry points when Claude Code knows the server exists but needs help selecting the right workflow.

## Notes

- Keep the server on Windows.
- Do not wrap `WpfDevTools.Mcp.Server.exe` with extra stdout logging.
- Start with `connect()` in the common case. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates or when you need explicit target metadata first.
- If `connect` fails, check server bitness, bootstrapper bitness, and target bitness together.
- If the target app is elevated, start Claude Code as administrator so the STDIO-launched MCP server can attach at the same integrity level.
