# WPF DevTools MCP Server

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-SDK%20v1.0-blue)](https://modelcontextprotocol.io/)
[![Tests](https://img.shields.io/badge/tests-3600%2B-brightgreen)]()
[![Coverage](https://codecov.io/gh/Evanlau1798/wpf-devtools-mcp/branch/master/graph/badge.svg)](https://codecov.io/gh/Evanlau1798/wpf-devtools-mcp)

`wpf-devtools-mcp` is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications. It gives AI clients WPF-native visibility into visual trees, bindings, dependency properties, routed events, MVVM state, layout, screenshots, and controlled runtime mutation.

Canonical source repository: this checkout.
Planned public repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
Planned public releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)

## Current State

- Shipping transport is STDIO only, wired through `WithStdioServerTransport()`.
- The server uses `Host.CreateEmptyApplicationBuilder(...)` so default console logging cannot pollute `stdout`.
- Use MCP tool discovery for input schemas.
- Use `wpf://contracts/response` for machine-readable response contracts.
- `structuredContent`, `navigation.recommended`, and compatibility `nextSteps` are the primary agent-facing response surfaces; failures may include `errorCode` and `errorData`.
- HTTP/SSE is planned, not included in the current server binary.
- The server ships 64 MCP tools; the full catalog lives in [docfx/reference/tools/index.md](docfx/reference/tools/index.md).

## Start Here

| Need | Canonical page |
| --- | --- |
| Full documentation entrypoint | [docfx/index.md](docfx/index.md) |
| First setup flow | [docfx/quickstart/index.md](docfx/quickstart/index.md) |
| AI client registration matrix | [docfx/quickstart/ai-agent-clients.md](docfx/quickstart/ai-agent-clients.md) |
| SDK-hosted app integration | [docfx/quickstart/sdk-hosted-inspector.md](docfx/quickstart/sdk-hosted-inspector.md) |
| Deployment and package layout | [docfx/production/deployment.md](docfx/production/deployment.md) |
| Security model | [docfx/production/security.md](docfx/production/security.md) |
| Compatibility constraints | [docfx/production/compatibility-matrix.md](docfx/production/compatibility-matrix.md) |
| Examples | [EXAMPLES.md](EXAMPLES.md) |
| Maintainer release flow | [RELEASING.md](RELEASING.md) |

## Install In Short

> **Public endpoint status:** Public release endpoints are not yet anonymously reachable. Until the GitHub repository, Releases page, latest-release API, raw installer URL, and installer alias all pass anonymous smoke checks, do not use remote one-line install commands from this documentation.

For first-time setup, prefer a published release or a locally generated package over source-tree startup. Review `scripts/online-installer.ps1`, then install a verified local package archive with machine-readable output:

When an AI agent is driving setup, start with the read-only plan flow in [AGENT_INSTALL.md](AGENT_INSTALL.md) or [docfx/guides/agent-assisted-install.md](docfx/guides/agent-assisted-install.md). The agent should run `-Action plan -OutputJson`, summarize the plan JSON, and wait for user confirmation before any filesystem mutation.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

The reviewed installer validates archive integrity before extraction and installs the extracted packaged payload through the reviewed installer/helper flow; omit `-Architecture` unless you intentionally need a different package because the installer detects the system architecture.

Manual package fallback requires `SHA256SUMS.txt` and `release-assets.json` verification before extraction. If those verified sidecars are no longer adjacent when running `run.bat`, provide `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` as the required thumbprint trust root; `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only an additional constraint after the thumbprint is pinned.

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
- `WPFDEVTOOLS_AUTH_SECRET`, `WPFDEVTOOLS_CERT_DIR`, and `WPFDEVTOOLS_CERT_THUMBPRINT` coordinate authenticated and encrypted named-pipe transport.

Detailed environment-variable behavior lives in [SECURITY.md](SECURITY.md), [docfx/production/security.md](docfx/production/security.md), and [docfx/reference/configuration.md](docfx/reference/configuration.md).

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

## Development

- Production code and tests aim to stay below 500 lines per file.
- New features and fixes should follow TDD: failing test first, minimal implementation second, refactor third.
- Build and test separately to avoid file-lock issues.

## License

MIT. DLL injection code includes Snoop-based components under Ms-PL attribution.
