# OpenAI Codex and Codex CLI Setup

Use this guide if you want to drive WPF DevTools from OpenAI Codex. The quickest path is to install Codex CLI, add the MCP server once, and then reuse the same registration from Codex workflows.

Before you start, complete [5-Minute Setup](index.md) through Step 5 so the server and bootstrapper are already built.

## One-command install for Codex CLI

```powershell
npm install -g @openai/codex
```

After installation, start Codex once and complete sign-in if needed:

```powershell
codex
```

## One-command MCP registration

Recommended: open PowerShell in the repository root and run:

```powershell
$RepoRoot = (Get-Location).Path
codex mcp add wpf-devtools -- dotnet run --project "$RepoRoot\src\WpfDevTools.Mcp.Server" --no-build
```

This avoids hard-coding a drive letter and works whether the repository lives on `C:`, `D:`, `E:`, or another volume.

If you are running the command from another folder, use an explicit absolute path instead:

```powershell
codex mcp add wpf-devtools -- dotnet run --project "<ABSOLUTE_PATH_TO_REPO>\src\WpfDevTools.Mcp.Server" --no-build
```

This writes the MCP server entry into Codex's shared configuration.

## Verify the registration

```powershell
codex mcp list
```

## Recommended first prompt

```text
List WPF processes, connect to the test app, ping it, and summarize the root visual tree.
```

## Using the Codex IDE extension

The Codex documentation uses the same MCP configuration file for Codex clients. In practice, the easiest workflow is:

1. install Codex CLI
2. run `codex mcp add ...` once
3. restart the Codex client or IDE extension
4. verify that `wpf-devtools` appears in the MCP server list

That gives you a single registration path instead of maintaining separate MCP config files per Codex surface.

## Important Windows note

OpenAI updates Codex client support frequently, so check the latest official Codex documentation for current Windows support details.

For this project specifically:

- prefer running Codex from a Windows shell if it works in your environment
- otherwise prefer the Codex IDE extension while keeping the MCP server on Windows
- do not move the WPF DevTools server itself into Linux or WSL if you need to inspect native Windows WPF processes

## Project-specific notes

- `connect` succeeds only when the bootstrapper architecture matches the target process architecture.
- Keep `stdout` clean; MCP over STDIO can break if wrappers print extra text.
- Use `Debug` builds for local unsigned development and signed `Release` builds for production.

## Related guides

- [AI Agent Clients](ai-agent-clients.md)
- [Cursor and VS Code](cursor-vscode.md)
- [AI Agent Guide](../guides/ai-agent-guide.md)
