# 5-Minute Setup

This quickstart is optimized for the current public distribution model: use the release package published on GitHub Releases, register the installed executable with your MCP client, and verify the first live WPF session with a scene-first workflow.

## Prerequisites

- Windows 10 or later
- A live WPF application running under the same user account as the MCP server
- An architecture choice that matches the target process: `x64`, `x86`, or `arm64`

## Architecture rule first

`connect` succeeds only when the server process architecture and bootstrapper architecture both match the target process architecture.

- `x64` target -> install and run the `x64` package
- `x86` target -> install and run the `x86` package
- `arm64` target -> install and run the `arm64` package

## Step 1: Choose an install path

### Online installer

Review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1), then bootstrap from the latest published release package:

```powershell
$architecture = 'x64'
$version = (Invoke-RestMethod 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest').tag_name.TrimStart('v')
$assetName = "release_${version}_win-$architecture.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wpf-devtools-install-" + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $tempRoot $assetName
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
Invoke-WebRequest "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force
powershell -ExecutionPolicy Bypass -File (Join-Path $tempRoot 'bin\install.ps1')
```

Client-specific example:

```powershell
$architecture = 'x64'
$version = (Invoke-RestMethod 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest').tag_name.TrimStart('v')
$assetName = "release_${version}_win-$architecture.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wpf-devtools-install-" + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $tempRoot $assetName
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
Invoke-WebRequest "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force
powershell -ExecutionPolicy Bypass -File (Join-Path $tempRoot 'bin\install.ps1') -Version latest -Architecture $architecture -Client claude-code -NonInteractive -Force -OutputJson
```

### Manual release package

1. Open [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Download `release_<version>_win-x64.zip`, `release_<version>_win-x86.zip`, or `release_<version>_win-arm64.zip`.
3. Extract the archive.
4. Run `run.bat` from the extracted folder.

### Local script invocation

If you prefer to run the same installer from a local clone instead of `irm | iex`, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -NonInteractive -Force -OutputJson
```

## Step 2: Confirm the installed executable path

After installation, the fallback executable path when no previous live install root is reused is:

```text
%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## Step 3: Register the installed executable

The installer writes ready-to-copy registration artifacts under:

```text
<InstallRoot>\<arch>\client-registration\
```

If you omit `-InstallRoot`, the installer first reuses the last live install root when possible and falls back to `%APPDATA%\WpfDevToolsMcp` only when no reusable install root is available. Use the generated `client-registration` artifacts as the source of truth for the resolved path.

If you register manually, always point your client at the installed `wpf-devtools-<arch>.exe` selected by the generated `client-registration` artifacts, not a source-tree `dotnet run` command.

## Step 4: Start or keep your WPF target running

The server only inspects live WPF processes. Start the app first, then launch the MCP client.

## Step 5: Verify the first session

Use this sequence in your MCP client:

1. `connect`
2. If auto-discovery reports multiple candidates, `get_processes(windowFilter)` and retry `connect(processId)`
3. `get_ui_summary(depthMode: "semantic")`
4. `get_element_snapshot` or `get_visual_tree` only if the summary is still insufficient
5. `ping` only if you want an explicit health check
6. After each diagnostic, interaction, or mutation, follow `navigation.recommended` first; use `nextSteps` as the compatibility fallback for clients that do not surface navigation yet

Healthy first-run signs:

- `connect()` succeeds immediately when there is only one visible WPF target
- if multiple targets exist, `get_processes(windowFilter)` returns the correct candidate list
- `get_ui_summary` returns a stable semantic summary of the root scene

## Fast useful prompt for an AI client

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Need deeper installation details?

- [AI Agent Clients](ai-agent-clients.md)
- [Deployment Guide](../production/deployment.md)
- [Release Layout](../production/release-layout.md)
