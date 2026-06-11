# WPF DevTools MCP Server

[![License: Apache-2.0](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-SDK%20v1.0-blue)](https://modelcontextprotocol.io/)
[![Coverage](https://codecov.io/gh/Evanlau1798/wpf-devtools-mcp/branch/master/graph/badge.svg)](https://codecov.io/gh/Evanlau1798/wpf-devtools-mcp)

`wpf-devtools-mcp` is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications. The canonical repository is [Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp), and it gives AI clients WPF-native visibility into visual trees, bindings, dependency properties, routed events, MVVM state, layout, screenshots, and controlled runtime mutation.

## Current State

- Shipping transport is STDIO only, wired through `WithStdioServerTransport()`.
- The server uses `Host.CreateEmptyApplicationBuilder(...)` so default console logging cannot pollute `stdout`.
- Use MCP tool discovery for input schemas.
- Use `wpf://contracts/response` for machine-readable response contracts.
- `structuredContent`, `navigation.recommended`, and compatibility `nextSteps` are the primary agent-facing response surfaces; failures may include `errorCode` and `errorData`.
- HTTP/SSE is planned, not included in the current server binary.
- The server ships 64 MCP tools; the full catalog lives in the [DocFX tool reference](https://wpf-mcptools.evanlau1798.com/reference/tools/).

## Start Here

| Need | Canonical page |
| --- | --- |
| Full documentation entrypoint | [DocFX home](https://wpf-mcptools.evanlau1798.com/) |
| First setup flow | [Quickstart](https://wpf-mcptools.evanlau1798.com/quickstart/) |
| AI client registration matrix | [AI agent clients](https://wpf-mcptools.evanlau1798.com/quickstart/ai-agent-clients.html) |
| SDK-hosted app integration | [SDK-hosted inspector](https://wpf-mcptools.evanlau1798.com/quickstart/sdk-hosted-inspector.html) |
| Deployment and package layout | [Deployment](https://wpf-mcptools.evanlau1798.com/production/deployment.html) |
| Security model | [Security](https://wpf-mcptools.evanlau1798.com/production/security.html) |
| Compatibility constraints | [Compatibility matrix](https://wpf-mcptools.evanlau1798.com/production/compatibility-matrix.html) |
| Examples | [EXAMPLES.md](EXAMPLES.md) |
| Maintainer release flow | [RELEASING.md](RELEASING.md) |

## Install In Short

The published release install command, after the versioned GitHub Release assets and sidecars are uploaded: `irm https://installer.wpf-mcptools.evanlau1798.com | iex`. The HTTPS alias is the installer redirect; the DocFX site is hosted at `https://wpf-mcptools.evanlau1798.com/`. The installer alias resolves the reviewed `scripts/online-installer.ps1` entrypoint and requires `release_<version>_win-<arch>.zip`, `SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `release-evidence.json`.

For GitHub pre-release E2E, download the reviewed online installer from the public installer alias and install the selected pre-release assets without cloning the repository:

```powershell
$e2eRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-mcp-e2e'; New-Item -ItemType Directory -Force -Path $e2eRoot | Out-Null
$installerPath = Join-Path $e2eRoot 'online-installer.ps1'
$installerDownload = @{ Uri = 'https://installer.wpf-mcptools.evanlau1798.com/'; OutFile = $installerPath }; if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('UseBasicParsing')) { $installerDownload.UseBasicParsing = $true }; Invoke-WebRequest @installerDownload
$installRoot = Join-Path $e2eRoot 'installed-wpf-devtools'; $workingRoot = Join-Path $e2eRoot 'installer-work'
powershell -ExecutionPolicy Bypass -File $installerPath -Version latest -Prerelease -Architecture x64 -Client other -InstallRoot $installRoot -WorkingRoot $workingRoot -NonInteractive -Force -OutputJson
```

Pre-release E2E still requires the GitHub pre-release to contain the matching archive and sidecars. For first-time setup from a staged archive, review `scripts/online-installer.ps1`, then install a verified local package archive with machine-readable output. When an AI agent drives setup, start with [AGENT_INSTALL.md](AGENT_INSTALL.md) or the [agent-assisted install guide](https://wpf-mcptools.evanlau1798.com/guides/agent-assisted-install.html), run `-Action plan -OutputJson`, and wait for user confirmation before mutation.

For real local `connect()` E2E, launch the packaged executable under `<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe`; a source-tree server launch can prove `tools/list`, but it does not provide the packaged `bin\inspectors` and `bin\bootstrapper` sidecar layout needed for raw-injection E2E. Exhaustive 64-tool automation should either pace requests or set `WPFDEVTOOLS_RATE_LIMIT_RPM=10000` for that local test session. When `-InstallRoot` is overridden, payloads and registration artifacts go under that root, but installer state tracking remains in `%APPDATA%\WpfDevToolsMcp\installer-state.json`.

If you run a direct MCP STDIO smoke test, send one newline-delimited JSON (NDJSON) JSON-RPC message per line. Do not use `Content-Length` framed messages with this server's STDIO transport; use [AGENT_INSTALL.md](AGENT_INSTALL.md) or the [DocFX quickstart](https://wpf-mcptools.evanlau1798.com/quickstart/) for the full raw smoke sequence.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

The reviewed installer validates archive integrity before extraction and installs the extracted packaged payload through the reviewed installer/helper flow; omit `-Architecture` unless you intentionally need a different package because the installer detects the system architecture.

Manual package fallback requires `SHA256SUMS.txt` and `release-assets.json` verification before extraction. Production payload signature verification still requires an independent `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`; adjacent sidecars prove archive provenance but do not replace signer trust. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only an additional constraint after the thumbprint is pinned.

`run.bat` requests elevation when the current shell is not already elevated. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when CLI registration must stay unelevated; for `claude-code` and `codex`, register manually after install if elevated CLI discovery is blocked. For raw injection, the server process architecture must match the target process. Architecture matching is mandatory for raw injection/bootstrapper fallback. SDK-hosted reuse communicates over named pipes and does not require matching process bitness once the target-side host is already running.

## Runtime Model

When you own the target application, prefer SDK-hosted Inspector reuse with `InspectorSdk.Initialize()`. When you need zero-instrumentation diagnostics, raw injection remains the fallback path.

`connect()` first tries to reuse a compatible SDK-hosted Inspector, then falls back to the raw injection path only when policy allows it. SDK reuse requires both `WPFDEVTOOLS_AUTH_SECRET` and the same local absolute directory in `WPFDEVTOOLS_CERT_DIR` on both sides; the default-hardened MCP server will not reuse a plaintext SDK host.

For local development, use a Debug build and build native bootstrapper binaries for the same architecture as the target process. Debug builds can skip local DLL signature verification only under the trusted-root policy; production Release payloads require signing and signer validation.

## Security Defaults

The MCP client is untrusted by default. Security decisions are enforced by server-side policy gates before process discovery details, UI text, screenshots, ViewModel values, or runtime mutations are returned; policy-denied process targets are redacted instead of disclosed.

`WPFDEVTOOLS_MCP_ALLOWED_TARGETS` must contain the reviewed target's exact local absolute executable path before a successful `connect()` workflow; unset values fail closed with `SecurityError`, while relative or malformed configured entries fail closed with `InvalidPolicyConfiguration`.

The injection-based transport is hardened by default. Key runtime gates:

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` restricts every `connect()` target before SDK-hosted reuse or raw injection; malformed configured entries return `InvalidPolicyConfiguration`.
- `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` explicitly allowlists raw-injection targets; malformed configured entries return `InvalidPolicyConfiguration`, while non-allowlisted targets return `SecurityError` with `requiresExplicitTargetOptIn`.
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` gates runtime mutation, interaction, render measurement, and session state-consuming tools such as `capture_state_snapshot` and `drain_events`.
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` gates `element_screenshot`.
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS` gates target UI text, DependencyProperty and binding values, event payloads, tree/scene summaries, and runtime state snapshots.
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` gates ViewModel inspection tools.
- `WPFDEVTOOLS_AUTH_SECRET`, `WPFDEVTOOLS_CERT_DIR`, and `WPFDEVTOOLS_CERT_THUMBPRINT` coordinate authenticated and encrypted named-pipe transport; explicit auth secrets must be base64 encoded and at least 32 decoded bytes (256 bits).

Detailed environment-variable behavior lives in [SECURITY.md](SECURITY.md), [DocFX security](https://wpf-mcptools.evanlau1798.com/production/security.html), and [DocFX configuration](https://wpf-mcptools.evanlau1798.com/reference/configuration.html).

## Operator Checklist

1. Install a signed release package and keep `SHA256SUMS.txt` plus release metadata adjacent during verification.
2. Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to the exact local absolute target executable path before discovery or `connect()`.
3. Choose SDK-hosted Inspector reuse for owned apps, or raw injection only when `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` explicitly allows that executable.
4. Enable only the gates needed for the session, including `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS`, screenshots, ViewModel inspection, or destructive tools.
5. Run a smoke workflow: `connect()`, `ping`, `get_ui_summary`, one safe read, and restore any mutation from a captured snapshot.
6. Inspect stderr/file logs and structured `errorCode`/`errorData` before widening allowlists or gates.
7. Revoke or rotate `WPFDEVTOOLS_AUTH_SECRET`, certificate material, and thumbprint pins after shared or temporary sessions.
8. Uninstall/cleanup packages, temporary cert directories, and client registrations when the diagnostic window ends.

## Typical MCP workflow

1. Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to the target WPF executable's exact local absolute executable path.
2. Call `connect()` and let the server auto-discover when only one allowlisted visible WPF target is available.
3. Use `get_processes(windowFilter)` only for disambiguation or metadata-first selection.
4. Start with `get_ui_summary` or `get_form_summary`; use `get_element_snapshot(elementId)` after discovering a concrete element.
5. Follow `navigation.recommended` first and treat `nextSteps` as the compatibility field for older clients.

## Maintainer Notes

Before creating or publishing a GitHub Release, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/tools/packaging/Preflight-Release.ps1 -VersionTag v0.1.0 -OutputJson
```

This validates the Release build, unit tests, package layout, and staged GitHub Release assets without uploading to GitHub. See [RELEASING.md](RELEASING.md) for the complete maintainer checklist.

## Official References

- [MCP build-server guide (C#)](https://modelcontextprotocol.io/docs/develop/build-server#c)
- [ModelContextProtocol C# SDK API](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.html)
- [Anthropic tool-definition guidance](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools)
- [Anthropic tool design guidance](https://www.anthropic.com/engineering/writing-tools-for-agents)
- [Anthropic advanced tool-use guidance](https://www.anthropic.com/engineering/advanced-tool-use) - reflected here through progressive discovery via `wpf://contracts/tools`, compact startup guidance, and reviewed tool examples.

## Development

- Production code and tests aim to stay below 500 lines per file.
- The repository has a large automated test suite; use `dotnet test --no-build --list-tests` on the built unit, release-unit, and integration test projects for current counts.
- New features and fixes should follow TDD: failing test first, minimal implementation second, refactor third.
- Build and test separately to avoid file-lock issues.

## License

WPF DevTools MCP Server is licensed under the Apache License, Version 2.0. Commercial use, proprietary integration, modification, redistribution, and sale are permitted under Apache-2.0.

When distributing this project or derivative works, preserve the Apache-2.0 license text, copyright notices, and the attribution notices in [NOTICE](NOTICE). Attribution may appear wherever third-party software notices are normally displayed; it does not need to appear in the main UI, homepage, or marketing materials.

Project names, logos, and related brand identifiers may not be used to imply official endorsement, partnership, certification, or sponsorship without prior written permission; see [TRADEMARK.md](TRADEMARK.md). DLL injection code includes Snoop-based components under Ms-PL attribution; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
