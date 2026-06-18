# WPF DevTools MCP Server

Looking for Traditional Chinese docs? Start at [繁體中文文件](zh-tw/index.md).

WPF DevTools MCP Server is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications. It gives MCP clients WPF-native visibility into visual trees, bindings, dependency properties, routed events, MVVM state, layout, screenshots, and controlled runtime mutation.

## Install

Preview pre-release installer until the first stable GitHub Release is published:

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

The installer resolves the reviewed online installer for the selected public pre-release, verifies the versioned package metadata, and installs the packaged executable. Current public onboarding uses `-Prerelease`; after the first stable GitHub Release is published, stable installs can omit that switch. For manual installation, download `release_<version>_win-<arch>.zip`, verify the adjacent sidecars, extract the package, and run `run.bat` from the extracted folder.

For manual production review, keep these files adjacent to the archive before extraction: `SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `package-sbom.spdx.json`.

`release-sbom.spdx.json` describes the release asset/archive inventory. `package-sbom.spdx.json` describes the package, dependency, script, assembly, and payload contents. Neither replaces signer trust; production payload signature verification still requires `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`.

## Choose your path

| I want to... | Start here |
| --- | --- |
| Read the full guide in Traditional Chinese | [繁體中文文件](zh-tw/index.md) |
| Install and verify the first session | [5-Minute Setup](quickstart/index.md) |
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
