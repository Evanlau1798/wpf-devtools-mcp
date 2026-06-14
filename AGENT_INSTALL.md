# Agent Install Contract

This file is the short, raw-link-friendly contract for AI agents. The full guide is [docfx/guides/agent-assisted-install.md](docfx/guides/agent-assisted-install.md).

## Boundary

Do not install yet. First produce a plan, show what will change, and get explicit user confirmation before mutation.

Supported client ids must stay synchronized with the installer:

- `claude-code`
- `codex`
- `cursor`
- `vscode`
- `visual-studio`
- `claude-desktop`
- `other`

## Required Flow

1. Confirm the host is Windows and detect `x64`, `x86`, or `arm64`.
2. Run the read-only installer discovery plan without writing files:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Action plan -OutputJson
   ```

   Expected JSON shape:

   ```json
   {
     "action": "plan",
     "contractVersion": 1,
     "platform": "windows",
     "version": "latest",
     "releaseChannel": "stable",
     "architecture": "x64",
     "client": "codex",
     "installRootDefault": "C:\\Users\\you\\AppData\\Roaming\\WpfDevToolsMcp",
     "preferredInstallRoot": "C:\\Users\\you\\AppData\\Roaming\\WpfDevToolsMcp",
     "fallbackInstallRoot": "C:\\Users\\you\\AppData\\Roaming\\WpfDevToolsMcp",
     "installRootSource": "default",
     "supportedClients": ["claude-code", "codex", "cursor", "vscode", "visual-studio", "claude-desktop", "other"],
     "detectedClients": [
       {
         "client": "codex",
         "available": true,
         "registrationStyle": "cli",
         "evidence": ["codex command"]
       },
       {
         "client": "other",
         "available": true,
         "registrationStyle": "artifact-only",
         "evidence": ["artifact-only fallback"]
       }
     ],
     "requiresUserConfirmationBeforeMutation": true,
     "mutatesFileSystem": false,
     "downloadsReleaseAssets": false,
     "runsClientRegistration": false,
     "mutationBoundary": "read-only discovery only; no download, install, registration, or filesystem mutation before user confirmation"
   }
   ```

3. Detect available MCP clients without writing files.
4. Ask the user to confirm version, architecture, install root, and client registration target.
5. Acquire the versioned release archive and sidecars: `release_<version>_win-<arch>.zip`, `SHA256SUMS.txt`, `release-assets.json`, and `release-sbom.spdx.json`.
6. Acquire `release-evidence.json` for published-release installs.
7. Verify the archive hash against `SHA256SUMS.txt`.
8. Verify canonical metadata from `release-assets.json`.
9. Verify `release-sbom.spdx.json` as the release asset SBOM sidecar. It is an asset-level release archive inventory, not a full package/dependency SBOM.
10. Enforce signer pin policy with `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` as the required thumbprint trust root. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only a certificate subject additional constraint after the thumbprint is pinned. Report the signer pin that was checked.
11. Only after confirmation, call the reviewed HTTPS alias, reviewed local `scripts/online-installer.ps1`, or package-local `run.bat`.

   Public HTTPS alias after GitHub Release assets exist:

   ```powershell
   irm https://installer.wpf-mcptools.evanlau1798.com | iex
   ```

   GitHub pre-release E2E path:

   ```powershell
   git clone https://github.com/Evanlau1798/wpf-devtools-mcp.git
   cd wpf-devtools-mcp
   $e2eRoot = (Resolve-Path .).Path
   $installRoot = Join-Path $e2eRoot '.e2e\installed-wpf-devtools'
   $workingRoot = Join-Path $e2eRoot '.e2e\installer-work'
   powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Prerelease -Architecture x64 -Client other -InstallRoot $installRoot -WorkingRoot $workingRoot -NonInteractive -Force -OutputJson
   ```

   Pre-release E2E requires a GitHub pre-release that contains the matching archive and sidecars, including `release-assets.json`, `SHA256SUMS.txt`, `release-sbom.spdx.json`, and `release-evidence.json`. Signed `Release` packaging still requires `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`. Use `-Client other` for validation-only E2E so the installer writes registration artifacts without updating a real MCP client.

   For runtime `connect()` validation, use the installed packaged executable under `<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe`. A source-tree `dotnet run` server is acceptable for discovery-only checks, but it does not prove the packaged `bin\inspectors` and `bin\bootstrapper` sidecar layout. For exhaustive 64-tool validation, either pace requests under the default 300 RPM limit or set `WPFDEVTOOLS_RATE_LIMIT_RPM=10000` for that local E2E session.

   `-InstallRoot` controls payload and registration output only. The installer still records live install state under `%APPDATA%\WpfDevToolsMcp\installer-state.json` so later runs can discover a reusable install root.

   Direct MCP STDIO smoke tests must send one newline-delimited JSON (NDJSON) JSON-RPC message per line. Do not use `Content-Length` framed messages with this server's STDIO transport. The large `tools/list` schema payload requires a real JSON parser such as Python, PowerShell 7, or .NET.

   Minimal NDJSON smoke sequence:

   ```jsonl
   {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"external-e2e","version":"1.0"}}}
   {"jsonrpc":"2.0","method":"notifications/initialized"}
   {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
   {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"connect","arguments":{}}}
   {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_ui_summary","arguments":{"depthMode":"semantic"}}}
   ```

   Reviewed local command shape after confirmation:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 `
     -PackageArchivePath .\release\release_<version>_win-<arch>.zip `
     -TrustedReleaseMetadataDirectory .\release `
     -Architecture <arch> `
     -Client <client-id> `
     -InstallRoot "<confirmed-install-root>" `
     -NonInteractive -Force -OutputJson
   ```

12. Inspect generated `client-registration` artifacts and verify the installed executable path.
13. Report changed files or registrations without printing secrets.

## Prohibited

- Do not execute remote install commands before the matching GitHub Release assets exist and the user approves the plan.
- Do not modify client configuration before the user confirms the plan.
- Do not ask for private keys, PFX password values, GitHub secrets, or signing secrets.
- Do not store, print, upload, or forward secrets.
- Do not call self-signed certificates production-trusted; they are only for local/dev/test.

## Output Expected From An Agent

- Detected platform and architecture.
- Detected clients and selected client id.
- Release archive and sidecar paths.
- Hash, metadata, release asset SBOM, and signer pin verification result.
- The exact local installer command that will run after confirmation.
- Installed executable path and client registration artifact path.

## External E2E validation checklist

- Start from a fresh clone from GitHub, not the caller's local worktree.
- Read this file plus the DocFX quickstart before installing.
- Install the latest GitHub pre-release package with `-Version latest -Prerelease` plus explicit `-InstallRoot` and `-WorkingRoot`.
- Start the golden WPF TestApp from the clone.
- Launch the installed packaged MCP server over STDIO and send real JSON-RPC requests.
- Verify `tools/list` reports 64 tools.
- Verify `connect`, `get_active_process`, `get_ui_summary`, and one safe read tool.
- Report P0/P1 blockers separately from P2/P3 documentation or polish findings.
