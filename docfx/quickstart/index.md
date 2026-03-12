# 5-Minute Setup

This quickstart is optimized for the public install path: run the GitHub Pages bootstrap installer, let it download the published release package, register the installed executable with your MCP client, and verify against a live WPF process.

## Prerequisites

- Windows 10 or later.
- A live WPF application running under the same user account as the MCP server.
- An architecture choice that matches the target process: `x64`, `x86`, or `arm64`.

## Architecture rule first

`connect` succeeds only when the server process architecture and bootstrapper architecture both match the target process architecture.

- `x64` target -> install and run the `x64` package.
- `x86` target -> install and run the `x86` package.
- `arm64` target -> install and run the `arm64` package.

## Step 1: Run the one-command installer

Fastest path:

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

That command fetches the static bootstrap script from GitHub Pages. The script then downloads the matching `WpfDevTools-win-<arch>.zip` release asset and runs the packaged `setup.ps1` wizard.

If you want a deterministic non-interactive install for a specific client, use:

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code -NonInteractive -Force
```

Repository and Releases:

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)

## Step 2: Manual fallback if you do not want `irm | iex`

1. Download `WpfDevTools-win-x64.zip`, `WpfDevTools-win-x86.zip`, or `WpfDevTools-win-arm64.zip` from Releases.
2. Extract the archive.
3. Run the packaged `setup.ps1 -Force` from the extracted folder.

The extracted package also includes an included `install.ps1` for the lower-level copy/install flow when you want to automate around the package contents directly instead of using the setup wizard.

## Step 3: Confirm the installed executable path

After installation, the default executable path is typically:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## Step 4: Register the installed executable

The installer writes ready-to-copy commands under:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\client-registration\
```

If you want to register manually, point your client at `WpfDevTools.Mcp.Server.exe`.

## Step 5: Start or keep your WPF target running

The server only inspects live WPF processes. Start your app first, then launch the MCP client.

## Step 6: Verify the first session

Use this sequence in your MCP client:

1. `connect`
2. If auto-discovery reports multiple candidates, `get_processes(windowFilter)` and retry `connect(processId)`
3. `get_visual_tree`
4. `ping` only if you want an explicit health check

Healthy first-run signs:

- `connect()` succeeds immediately when there is only one visible WPF target.
- If multiple targets exist, `get_processes(windowFilter)` returns the correct candidate list.
- `get_visual_tree` returns the root window and child elements.

## Fastest useful prompt for an AI client

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, and summarize the first two levels of the visual tree.
```

## Need a source-based setup instead?

If you are contributing to the repository or debugging the server itself, use the contributor setup docs instead of the public install path.

Next: [AI Agent Clients](ai-agent-clients.md)
