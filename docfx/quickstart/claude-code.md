# Claude Code Setup

Claude Code is the fastest public path when you want a terminal-first agent workflow.

## 1. Install Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
```

## 2. Install WPF DevTools

Run the WPF DevTools installer first. After that, use the installed executable path:

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

## 4. Verify the registration

```powershell
claude mcp list
```

## 5. First useful prompt

```text
List WPF processes, connect to the target app, ping it, and show the first two levels of the visual tree.
```

## Notes

- Keep the server on Windows.
- Do not wrap `WpfDevTools.Mcp.Server.exe` with extra stdout logging.
- If `connect` fails, check the target bitness first.
