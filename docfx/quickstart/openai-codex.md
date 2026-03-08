# OpenAI Codex and Codex CLI Setup

Use this guide when you want the installed WPF DevTools server to be available from Codex workflows.

## 1. Install Codex CLI

```powershell
npm install -g @openai/codex
```

## 2. Install WPF DevTools

Fastest path:

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

One-command Codex-focused setup:

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients codex-cli -NonInteractive -Force
```

After installation, the default executable path is:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 3. Register the MCP server

Use the generated command from `client-registration\codex-cli.txt`, or run:

```powershell
codex mcp add wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

## 4. Verify the registration

```powershell
codex mcp list
```

## 5. First useful prompt

```text
List WPF processes, connect to the target app, ping it, and summarize the root visual tree.
```

## Notes

- Keep the MCP server on Windows even if your editor tooling spans multiple environments.
- If `connect` fails, check server bitness, bootstrapper bitness, and the target process bitness together.
- Keep `stdout` clean because Codex uses STDIO MCP transport.
