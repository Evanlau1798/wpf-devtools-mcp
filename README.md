# WPF DevTools MCP Server

[![License: Apache-2.0](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-SDK%20v1.4-blue)](https://modelcontextprotocol.io/)

`wpf-devtools-mcp` is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications. It exposes WPF-native diagnostics for visual/logical trees, bindings, dependency properties, routed events, layout, screenshots, MVVM state, and controlled runtime mutation.

## Status

- Windows and WPF targets only.
- STDIO MCP server transport.
- 77 MCP tools; use the [DocFX tool reference](https://wpf-mcptools.evanlau1798.com/reference/tools/) for the current catalog.
- Security defaults fail closed: targets, screenshots, sensitive reads, ViewModel inspection, raw injection, and destructive operations require explicit configuration.

## Install

Install the latest stable release:

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

Pinned pre-release install for beta or preview validation; replace the example with the selected public pre-release tag:

```powershell
$version = 'v1.0.0-beta.51'
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version $version -Prerelease
```

Both commands use the reviewed HTTPS installer alias and install versioned GitHub Release assets. Normal installs do not require downloading the release ZIP manually.

Beta pre-release assets may use SHA256 release metadata until paid Authenticode signing is available; stable releases remain Signed packages once signing is configured.

Manual package fallback: download the matching `release_<version>_win-<arch>.zip`, verify it with trusted release metadata, then extract the package before using package-local `run.bat`. Use the [Quickstart](https://wpf-mcptools.evanlau1798.com/quickstart/), [Deployment Guide](https://wpf-mcptools.evanlau1798.com/production/deployment.html), and [Release Layout](https://wpf-mcptools.evanlau1798.com/production/release-layout.html) for verification details.

## Security essentials

- Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to the reviewed target's exact local absolute executable path before `connect()`.
- Malformed `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` entries fail with `InvalidPolicyConfiguration`.
- Set `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` only when UI text, binding values, DependencyProperty values, or runtime state may leave the target process.
- Set `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` only when screenshot capture is approved.
- Set `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` only when ViewModel inspection is approved; this covers `get_viewmodel`, `get_commands`, `get_datacontext_chain`, `modify_viewmodel`, and `execute_command`, and also applies when `capture_state_snapshot`, `batch_mutate`, or `wait_for_dp_change_after_mutation` request ViewModel state.
- Set `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` only for approved mutation, interaction, render measurement, or session state-consuming tools such as `capture_state_snapshot` and `drain_events`.
- Raw injection fallback requires `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`; blocked targets return `SecurityError` with `requiresExplicitTargetOptIn`, and malformed entries fail with `InvalidPolicyConfiguration`.
- Treat the MCP client as untrusted by default; server-side policy gates keep denied target metadata redacted before disclosure.
- injection-based `connect()` uses persisted local HMAC secret and named-pipe TLS by default.
- `WPFDEVTOOLS_AUTH_SECRET` must be base64 encoded and decode to at least 32 decoded bytes (256 bits).
- SDK-hosted reuse requires both `WPFDEVTOOLS_AUTH_SECRET` and the same local absolute `WPFDEVTOOLS_CERT_DIR` on both sides; the default-hardened server will not reuse plaintext SDK hosts.
- Architecture matching is mandatory for raw injection/bootstrapper fallback; SDK-hosted reuse communicates over named pipes.

## Documentation

- [DocFX home](https://wpf-mcptools.evanlau1798.com/)
- [繁體中文文件](https://wpf-mcptools.evanlau1798.com/zh-tw/)
- [5-Minute Setup](https://wpf-mcptools.evanlau1798.com/quickstart/)
- [AI Agent Clients](https://wpf-mcptools.evanlau1798.com/quickstart/ai-agent-clients.html)
- [UI Composer Tools](https://wpf-mcptools.evanlau1798.com/reference/tools/ui-composer.html)
- [SDK-Hosted Inspector](https://wpf-mcptools.evanlau1798.com/quickstart/sdk-hosted-inspector.html)
- [Security Model](https://wpf-mcptools.evanlau1798.com/production/security.html)
- [Release Layout](https://wpf-mcptools.evanlau1798.com/production/release-layout.html)
- [Maintainer Docs](https://wpf-mcptools.evanlau1798.com/contributors/)
- [Public-Path Runtime Security Checklist](https://wpf-mcptools.evanlau1798.com/contributors/public-path-runtime-security.html)

## Development

```powershell
dotnet restore WpfDevTools.sln --locked-mode
dotnet build WpfDevTools.sln -c Debug
```

Maintainer release and packaging procedures live in [RELEASING.md](RELEASING.md). Agent-driven setup rules live in [AGENT_INSTALL.md](AGENT_INSTALL.md).

## License

Apache License, Version 2.0. Commercial use is allowed under the Apache-2.0 terms. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [TRADEMARK.md](TRADEMARK.md).
