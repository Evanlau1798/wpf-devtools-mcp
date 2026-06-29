# WPF DevTools MCP Server

Looking for Traditional Chinese docs? Start at [繁體中文文件](zh-tw/index.md).

WPF DevTools MCP Server is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications. It gives MCP clients WPF-native visibility into visual trees, bindings, dependency properties, routed events, MVVM state, layout, screenshots, and controlled runtime mutation.

## Install

Install the latest stable release:

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

The installer resolves the reviewed online installer, verifies the versioned package metadata for `release_<version>_win-<arch>.zip`, and installs the packaged executable.

ARM64 archives may be published as preview assets, but they are not guaranteed stable because practical Windows-on-ARM runtime validation hardware is not currently available.

Manual verified install is a separate path for reviewed archives. Keep `SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `package-sbom.spdx.json` next to the archive, then follow [Manual Verified Install](quickstart/manual-install.md).

Release trust is still explicit on that path: `Signed` packages require `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`, while `ReleaseChecksumOnly` beta packages rely on SHA256 release metadata. Use package-local `run.bat` only after sidecar verification.

## Choose your path

| I want to... | Start here |
| --- | --- |
| Read the full guide in Traditional Chinese | [繁體中文文件](zh-tw/index.md) |
| Install and verify the first session | [5-Minute Setup](quickstart/index.md) |
| Install from a reviewed local archive | [Manual Verified Install](quickstart/manual-install.md) |
| Register an MCP client | [AI Agent Clients](quickstart/ai-agent-clients.md) |
| Reuse an Inspector hosted by my app | [SDK-Hosted Inspector](quickstart/sdk-hosted-inspector.md) |
| Review security gates | [Security Model](production/security.md) |
| Deploy a reviewed package | [Deployment Guide](production/deployment.md) |
| Understand release assets | [Release Layout](production/release-layout.md) |
| Browse tools | [Tool Reference Overview](reference/tools/index.md) |

## What you can do

- Discover and connect to a reviewed WPF target.
- Start with scene-level summaries such as `get_ui_summary` and `get_form_summary`.
- Diagnose binding failures, DependencyProperty precedence, templates, routed events, layout state, and focus state.
- Use snapshots, diffs, and rollback-oriented workflows when mutation gates are explicitly enabled.
- Inspect ViewModel state only when the ViewModel inspection gate is approved.

## Scope and boundaries

- Transport: STDIO MCP server.
- Platform: Windows only.
- Target UI stack: WPF only.
- Persistence: runtime mutations do not write back to XAML.
- Security: policy gates run before process details, UI text, screenshots, ViewModel values, or mutation operations are returned.
- Transport hardening: injection-based sessions use persisted local HMAC secret and named-pipe TLS by default.

## Architecture at a glance

```text
AI Client
  -> MCP over STDIO
MCP Server
  -> named pipes with authenticated framing
Inspector host
  -> WPF Dispatcher and in-process APIs
Target WPF application
```
