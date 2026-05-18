# 5-Minute Setup

This quickstart is optimized for the pre-publication distribution model: use a locally generated and verified release package, register the installed executable with your MCP client, and verify the first live WPF session with a scene-first workflow.

## Prerequisites

- Windows 10 or later
- A live WPF application running under the same user account as the MCP server
- An architecture choice that matches the target process: `x64`, `x86`, or `arm64`
- The exact absolute executable path of the WPF target reviewed and listed in `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`; unset or malformed allowlist values fail closed before `connect()` attaches

## Architecture rule first

`connect` succeeds only when the server process architecture and bootstrapper architecture both match the target process architecture.

- `x64` target -> install and run the `x64` package
- `x86` target -> install and run the `x86` package
- `arm64` target -> install and run the `arm64` package

## Step 1: Choose an install path

### Reviewed local package installer

> **Public endpoint status:** Public release endpoints are not yet anonymously reachable. Until the GitHub repository, Releases page, latest-release API, raw installer URL, and installer alias all pass anonymous smoke checks, use a locally generated release package or a source checkout instead of remote one-line install commands.

Review `scripts/online-installer.ps1` as the canonical source first. That installer can install a local package archive, validates archive integrity before extraction, and then installs the extracted packaged payload through the reviewed installer/helper flow.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

The default interactive flow asks for the release version, uses the current machine architecture (`x64`, `x86`, or `arm64`) as the default architecture, and then asks which MCP client registration to generate. When you omit `-Architecture`, the installer detects the system architecture; pass `-Architecture` only when you intentionally need to install a different package.

Client-specific automation example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

### Manual release package

1. Use a locally generated package, or after public endpoint smoke checks pass, open [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Download `release_<version>_win-x64.zip`, `release_<version>_win-x86.zip`, or `release_<version>_win-arm64.zip` together with `SHA256SUMS.txt` and `release-assets.json`.
3. Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.
4. Extract the archive.
5. Run `run.bat` from the extracted folder.

Before trusting the extracted package, keep the verified release sidecars beside the archive: `SHA256SUMS.txt` for the checksum and `release-assets.json` for the canonical release metadata. If the verified archive and those sidecars are no longer adjacent to the extracted package, set `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` (or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`) before launching `run.bat` so the local install still enforces an explicit signer pin.

`run.bat` requests elevation when the current shell is not already elevated and then launches the packaged `bin/install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.

### Local script invocation

If you prefer a package-local install instead of the reviewed script-first path, use the manual release package flow above.

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

1. Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` includes the target's exact absolute executable path.
2. `connect`
3. If auto-discovery reports multiple candidates, `get_processes(windowFilter)` and retry `connect(processId)`
4. `get_ui_summary(depthMode: "semantic")`
5. `get_element_snapshot` or `get_visual_tree` only if the summary is still insufficient
6. `ping` only if you want an explicit health check
7. After each diagnostic, interaction, or mutation, follow `navigation.recommended` first; use `nextSteps` as the compatibility fallback for clients that do not surface navigation yet

Healthy first-run signs:

- `connect()` succeeds when the target is allowlisted, architecture-compatible, and there is only one visible WPF target
- if multiple targets exist, `get_processes(windowFilter)` returns the correct candidate list
- `get_ui_summary` returns a stable semantic summary of the root scene

## Fast useful prompt for an AI client

```text
After WPFDEVTOOLS_MCP_ALLOWED_TARGETS includes the running WPF app's exact absolute executable path, connect to it, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Need deeper installation details?

- [AI Agent Clients](ai-agent-clients.md)
- [Deployment Guide](../production/deployment.md)
- [Release Layout](../production/release-layout.md)
