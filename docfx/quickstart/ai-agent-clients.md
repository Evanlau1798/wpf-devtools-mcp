# AI Agent Clients

This page helps you choose the fastest MCP client path for WPF DevTools.

Before you configure any client, finish the server-side setup in [5-Minute Setup](index.md): build the managed server, build the native bootstrapper for your target architecture, and run a WPF target app.

## Recommended paths

| Client | Best for | Install style | MCP registration style | Guide |
| --- | --- | --- | --- | --- |
| Claude Code | Terminal-first AI workflow with one-command setup | One command | One command | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | OpenAI agent workflow, CLI or IDE extension | One command | One command | [OpenAI Codex and Codex CLI](openai-codex.md) |
| Claude Desktop | Desktop chat app workflow | App install | JSON config file | [Claude Desktop](claude-desktop.md) |
| Cursor / VS Code | Editor-first workflow | App or extension install | MCP settings / JSON config | [Cursor and VS Code](cursor-vscode.md) |

## The command all clients ultimately run

All quick setup flows in this documentation resolve to the same STDIO server command:

```powershell
dotnet run --project C:\src\wpf-devtools-mcp\src\WpfDevTools.Mcp.Server --no-build
```

Replace `C:\src\wpf-devtools-mcp` with your actual clone path.

## First-session verification workflow

No matter which client you choose, verify the first session in this order:

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

A healthy first run means:

- the target WPF process appears in `get_processes`
- `connect` succeeds without architecture mismatch
- `ping` returns quickly
- `get_visual_tree` returns the root window and child elements

## WPF-specific notes for AI clients

- This server is Windows-only and targets running WPF processes.
- The MCP transport is STDIO, so wrappers must keep `stdout` clean.
- `connect` only succeeds when the bootstrapper architecture matches the target process architecture.
- Build the server before registering it in your AI client, especially when using `--no-build`.

Next: [Claude Code](claude-code.md) or [OpenAI Codex and Codex CLI](openai-codex.md)
