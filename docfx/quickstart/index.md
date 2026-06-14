# 5-Minute Setup

This quickstart is optimized for the pre-publication distribution model: use a locally generated and verified release package, register the installed executable with your MCP client, and verify the first live WPF session with a scene-first workflow.

## Prerequisites

- Windows 10 or later
- A live WPF application running under the same user account as the MCP server
- An architecture choice for the package you want to install: `x64`, `x86`, or `arm64`
- The exact local absolute executable path of the WPF target reviewed and listed in `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`; unset or malformed allowlist values fail closed before `connect()` attaches

## Architecture rule first

Architecture matching is mandatory for raw injection/bootstrapper fallback. Install the package that matches the target process when you need zero-instrumentation attach.

SDK-hosted reuse communicates over named pipes and does not require matching process bitness once the target-side host is already running.

- `x64` target -> install and run the `x64` package
- `x86` target -> install and run the `x86` package
- `arm64` target -> install and run the `arm64` package

## Step 1: Choose an install path

### Public HTTPS installer after release assets exist

Use the public installer after the versioned GitHub Release assets and sidecars exist for the release under test:

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

The HTTPS alias resolves the reviewed `scripts/online-installer.ps1` entrypoint. It requires the matching GitHub Release assets set: `release_<version>_win-<arch>.zip`, `SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `release-evidence.json`.

### GitHub pre-release E2E online installer path

For external E2E before a public release is promoted, download the reviewed online installer from the public installer alias and install the latest GitHub pre-release without cloning the repository:

```powershell
$e2eRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-mcp-e2e'
New-Item -ItemType Directory -Force -Path $e2eRoot | Out-Null
$installerPath = Join-Path $e2eRoot 'online-installer.ps1'
$installerDownload = @{
    Uri = 'https://installer.wpf-mcptools.evanlau1798.com/'
    OutFile = $installerPath
}
if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('UseBasicParsing')) {
    $installerDownload.UseBasicParsing = $true
}
Invoke-WebRequest @installerDownload
$installRoot = Join-Path $e2eRoot 'installed-wpf-devtools'
$workingRoot = Join-Path $e2eRoot 'installer-work'
powershell -ExecutionPolicy Bypass -File $installerPath -Version latest -Prerelease -Architecture x64 -Client other -InstallRoot $installRoot -WorkingRoot $workingRoot -NonInteractive -Force -OutputJson
```

Pre-release E2E requires the GitHub pre-release to contain the matching package archive and sidecars, including `release-assets.json`, `SHA256SUMS.txt`, `release-sbom.spdx.json`, and `release-evidence.json`. Signed `Release` packaging still requires `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`. The validation snippet uses `-Client other` so automation writes artifact-only registration instead of changing a real MCP client configuration.

For real local `connect()` E2E, launch the installed packaged executable under `<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe`. Running the server directly from the source tree can verify `initialize`, `tools/list`, resources, and STDIO framing, but it does not prove the packaged `bin\inspectors` and `bin\bootstrapper` sidecar layout required by raw injection. Exhaustive 64-tool validation should either pace requests under the default 300 requests/minute limit or set `WPFDEVTOOLS_RATE_LIMIT_RPM=10000` for that local E2E session.

If you run a direct MCP STDIO smoke test, send one newline-delimited JSON (NDJSON) JSON-RPC message per line. Do not use `Content-Length` framed messages with this server's STDIO transport. The large `tools/list` schema payload requires a real JSON parser such as Python, PowerShell 7, or .NET.

Minimal NDJSON smoke sequence:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"external-e2e","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"connect","arguments":{}}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_ui_summary","arguments":{"depthMode":"semantic"}}}
```

### Reviewed local package installer

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
2. Download `release_<version>_win-x64.zip`, `release_<version>_win-x86.zip`, or `release_<version>_win-arm64.zip` together with `SHA256SUMS.txt`, `release-assets.json`, and `release-sbom.spdx.json`.
3. Verify the archive with `SHA256SUMS.txt`, `release-assets.json`, and `release-sbom.spdx.json` before extraction.
4. Extract the archive.
5. Run `run.bat` from the extracted folder.

Before trusting the extracted package, keep the verified release sidecars beside the archive: `SHA256SUMS.txt` for the checksum, `release-assets.json` for the canonical release metadata, and `release-sbom.spdx.json` for the release asset SBOM. The SBOM sidecar is an asset-level release archive inventory, not a full package/dependency SBOM. Production payload signature verification still requires an independent `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`; adjacent sidecars prove archive provenance but do not replace signer trust. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only an additional constraint after the thumbprint is pinned.

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

If you pass `-InstallRoot`, payloads and registration artifacts are written under that root, but installer state tracking remains under `%APPDATA%\WpfDevToolsMcp\installer-state.json`. That state file records the last live install evidence and may be reused by later installer runs.

If you register manually, always point your client at the installed `wpf-devtools-<arch>.exe` selected by the generated `client-registration` artifacts, not a source-tree `dotnet run` command.

## Step 4: Start or keep your WPF target running

The server only inspects live WPF processes. Start the app first, then launch the MCP client.

If you own the target app source code, prefer the [SDK-hosted Inspector quickstart](sdk-hosted-inspector.md) before relying on raw injection; raw injection remains the fallback path for zero-instrumentation diagnostics.

## Step 5: Verify the first session

Before the first `connect()` attempt, make the policy profile explicit for the current diagnostic session:

```powershell
$targetExe = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $targetExe
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $targetExe
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

`WPFDEVTOOLS_MCP_ALLOWED_TARGETS` controls which process metadata and connection targets are visible. `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` is also required when the session must use raw injection instead of an already-running SDK-hosted Inspector. `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` is required before scene, form, tree, binding, DP, or state reads can return target UI text or runtime values.

Use this sequence in your MCP client:

1. Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` includes the target's exact local absolute executable path.
2. `connect`
3. If auto-discovery reports multiple candidates, `get_processes(windowFilter)` and retry `connect(processId)`
4. `get_ui_summary(depthMode: "semantic")`
5. `get_visual_tree`, or `get_element_snapshot(elementId)` after a concrete elementId is known, only if the summary is still insufficient
6. `ping` only if you want an explicit health check
7. After each diagnostic, interaction, or mutation, follow `navigation.recommended` first; use `nextSteps` as the compatibility fallback for clients that do not surface navigation yet

Minimal valid rollback guard before an interaction or mutation:

```powershell
$env:WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS = 'true'

tools/call capture_state_snapshot @{
    includeFocus = $true
    snapshotName = 'first-session-focus'
}
```

`capture_state_snapshot` intentionally requires at least `propertyNames`, `viewModelPropertyNames`, or `includeFocus`; an empty argument object returns a structured `MissingRequiredParameter` error.

Healthy first-run signs:

- `connect()` succeeds when the target is allowlisted, either injection-compatible or already exposing an SDK-hosted Inspector, and there is only one visible WPF target
- if multiple targets exist, `get_processes(windowFilter)` returns the correct candidate list
- `get_ui_summary` returns a stable semantic summary of the root scene

## Fast useful prompt for an AI client

```text
After WPFDEVTOOLS_MCP_ALLOWED_TARGETS and WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS include the running WPF app's exact local absolute executable path, and WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true is set for scene reads, connect to it, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Need deeper installation details?

- [AI Agent Clients](ai-agent-clients.md)
- [SDK-Hosted Inspector](sdk-hosted-inspector.md)
- [Deployment Guide](../production/deployment.md)
- [Release Layout](../production/release-layout.md)
