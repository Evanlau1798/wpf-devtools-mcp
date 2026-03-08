# Claude Code Setup

This is the fastest path if you want a terminal-first AI agent workflow with one-command installation and one-command MCP registration.

Before you start, complete [5-Minute Setup](index.md) through Step 5 so the server and bootstrapper are already built.

## One-command install on Windows

PowerShell:

```powershell
irm https://claude.ai/install.ps1 | iex
```

Alternative with WinGet:

```powershell
winget install Anthropic.ClaudeCode
```

## One-command MCP registration

Recommended: open PowerShell in the repository root and run:

```powershell
$RepoRoot = (Get-Location).Path
claude mcp add --transport stdio wpf-devtools -- dotnet run --project "$RepoRoot\src\WpfDevTools.Mcp.Server" --no-build
```

This avoids hard-coding a drive letter and works whether the repository lives on `C:`, `D:`, `E:`, or another volume.

If you are running the command from another folder, use an explicit absolute path instead:

```powershell
claude mcp add --transport stdio wpf-devtools -- dotnet run --project "<ABSOLUTE_PATH_TO_REPO>\src\WpfDevTools.Mcp.Server" --no-build
```

This registers the server under the name `wpf-devtools` and tells Claude Code to launch it over STDIO.

## Optional: project-scoped registration

If you want the MCP registration to live with the repository instead of your global machine config, open PowerShell in the repository root and run:

```powershell
$RepoRoot = (Get-Location).Path
claude mcp add --scope project --transport stdio wpf-devtools -- dotnet run --project "$RepoRoot\src\WpfDevTools.Mcp.Server" --no-build
```

Use this when you want teammates to see the intended MCP server setup from the repository itself.

## Verify that Claude Code sees the server

```powershell
claude mcp list
```

Then start Claude Code in the repository and use this first prompt:

```text
List WPF processes, connect to the test app, ping it, and show me the top two levels of the visual tree.
```

## If you are using the included test app

Keep these two terminals open:

Terminal 1:

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

Terminal 2 is managed by Claude Code through the MCP registration command above.

## Claude Code notes for this project

- Prefer `Debug` builds for local unsigned development.
- Keep the MCP server on Windows because the target WPF process and bootstrapper are Windows-native.
- Do not wrap the server with scripts that write logs to `stdout`.
- If `connect` fails, check process architecture first. The server bitness and target process bitness must match.

## Related guides

- [AI Agent Clients](ai-agent-clients.md)
- [Claude Desktop](claude-desktop.md)
- [AI Agent Guide](../guides/ai-agent-guide.md)
