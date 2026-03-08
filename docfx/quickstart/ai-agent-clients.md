# AI Agent Client Quickstart

This page helps you choose the shortest path to a usable MCP client for WPF DevTools.

Before configuring any client, finish the server-side steps in [5-Minute Setup](index.md): build the managed server, build the native bootstrapper for the target architecture, and start a WPF target process.

## Recommended paths

| Client | Best fit | Install style | MCP registration style | Guide |
| --- | --- | --- | --- | --- |
| Claude Code | Terminal-first workflow, one-command install and registration | One command | One command | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | OpenAI agent workflow, CLI or IDE extension | One command | One command | [OpenAI Codex and Codex CLI](openai-codex.md) |
| Claude Desktop | Desktop chat app workflow | App install | JSON config file | [Claude Desktop](claude-desktop.md) |
| Cursor / VS Code | Editor-first workflow | App or extension install | MCP settings / JSON config | [Cursor and VS Code](cursor-vscode.md) |

## Shared server command and path strategy

Every quickstart page eventually launches the same STDIO MCP server command.

If your shell is already in the repository root, use this pattern:

```powershell
$RepoRoot = (Get-Location).Path
dotnet run --project "$RepoRoot\src\WpfDevTools.Mcp.Server" --no-build
```

If your client stores static JSON configuration, use the same project path as a literal absolute path instead:

```powershell
dotnet run --project "<ABSOLUTE_PATH_TO_REPO>\src\WpfDevTools.Mcp.Server" --no-build
```

`<ABSOLUTE_PATH_TO_REPO>` can be on any Windows drive, for example `D:\dev\wpf-devtools-mcp` or `E:\repos\wpf-devtools-mcp`.

## First verification flow

No matter which client you choose, verify the first connection in this order:

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

Your first healthy run should show:

- `get_processes` lists the target WPF process
- `connect` succeeds without an architecture mismatch
- `ping` responds quickly
- `get_visual_tree` returns the root window and child elements

## WPF-specific reminders for AI clients

- This server only runs on Windows against a live WPF process.
- MCP transport is STDIO, so wrappers must not pollute `stdout`.
- `connect` succeeds only when the bootstrapper architecture matches the target process architecture.
- If you use `--no-build`, finish the build before registering the server with your AI client.

Next: [Claude Code](claude-code.md) or [OpenAI Codex and Codex CLI](openai-codex.md)
