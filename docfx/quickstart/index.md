# 5-Minute Setup

This quickstart is optimized for the public install path: download the published release package, run the included `install.ps1`, register the installed executable with your MCP client, and verify against a live WPF process.

## Prerequisites

- Windows 10 or later.
- A live WPF application running under the same user account as the MCP server.
- An architecture choice that matches the target process: `x64`, `x86`, or `arm64`.

## Architecture rule first

`connect` succeeds only when the server process architecture and bootstrapper architecture both match the target process architecture.

- `x64` target -> install and run the `x64` package.
- `x86` target -> install and run the `x86` package.
- `arm64` target -> install and run the `arm64` package.

## Step 1: Download the published release package

Open the Releases page and download the package that matches your target app architecture:

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)

Expected package names:

- `WpfDevTools-win-x64`
- `WpfDevTools-win-x86`
- `WpfDevTools-win-arm64`

Extract the archive to a local folder before running the installer.

## Step 2: Run the package-local installer

From the extracted package folder, run the included `install.ps1`.

```powershell
Set-Location C:\path\to\WpfDevTools-win-x64
.\install.ps1 -Force
```

The package-local flow uses the folder that already contains `manifest.json`, `WpfDevTools.Mcp.Server.exe`, and the release payload, so you do not need to pass `-PackagePath` for the normal public install path.

After installation, the default executable path is typically:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## Step 3: Register the installed executable

The installer writes ready-to-copy commands under:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\client-registration\
```

If you want to register manually, point your client at `WpfDevTools.Mcp.Server.exe`.

## Step 4: Start or keep your WPF target running

The server only inspects live WPF processes. Start your app first, then register or launch the MCP client.

## Step 5: Verify the first session

Use this sequence in your MCP client:

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

Healthy first-run signs:

- `get_processes` lists the target WPF process.
- `connect` succeeds without an architecture mismatch.
- `ping` responds quickly.
- `get_visual_tree` returns the root window and child elements.

## Fastest useful prompt for an AI client

```text
List WPF processes, connect to the target app, ping it, and summarize the first two levels of the visual tree.
```

## Need a source-based setup instead?

If you are contributing to the repository or debugging the server itself, use the contributor setup docs instead of the public install path.

Next: [AI Agent Clients](ai-agent-clients.md)
