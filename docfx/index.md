# WPF DevTools MCP Server

Looking for Traditional Chinese docs? Start at [繁體中文文件](zh-tw/index.md).

WPF DevTools MCP Server is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications through an injected in-process inspector. It is designed for scenarios where UI Automation is not enough: binding diagnostics, dependency property precedence, scene-level summaries, MVVM inspection, routed-event tracing, layout debugging, and controlled runtime mutation.

## Canonical sources

- Canonical source repository: this checkout
- Planned public repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Planned public releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)
- Online installer source: `scripts/online-installer.ps1` (maintainer source; review the version-matched release artifact before executing a published package)
- Release packaging source: `scripts/tools/packaging/Publish-Release.ps1`
- Installed-layout sources: `scripts/installer/Installer.Actions.ps1`, `scripts/installer/Installer.Registration.ps1`

`scripts/` is the canonical source of truth for installer and release behavior. This DocFX site documents those scripts; it does not define them.

## Install paths

### Verified local package path

> **Public endpoint status:** Public release endpoints are not yet anonymously reachable. Until the GitHub repository, Releases page, latest-release API, raw installer URL, and installer alias all pass anonymous smoke checks, use a locally generated release package or a source checkout instead of remote one-line install commands.

Review the canonical maintainer source first: `scripts/online-installer.ps1`. The reviewed installer can install a local package archive, validates archive integrity before extraction, and then installs the extracted packaged payload through the reviewed installer/helper flow.

Recommended local package example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

The default interactive flow asks for the release version, uses the current machine architecture (`x64`, `x86`, or `arm64`) as the default architecture, and then asks which MCP client registration to generate. When you omit `-Architecture`, the installer detects the system architecture; pass `-Architecture` only when you intentionally need to install a different package.

Client-specific automation example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

The repository entrypoint is still only the bootstrap layer; the actual install uses the verified extracted package payload through the reviewed installer/helper flow.

### Manual release package path

1. Use a locally generated package, or after public endpoint smoke checks pass, download `release_<version>_win-x64.zip`, `release_<version>_win-x86.zip`, or `release_<version>_win-arm64.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) together with `SHA256SUMS.txt` and `release-assets.json`.
2. Verify the downloaded archive against the release provenance sidecars before extraction. Confirm the asset hash matches `SHA256SUMS.txt` and the exact asset entry in `release-assets.json`. The reviewed online installer performs this verification automatically; the manual path does not.
3. Keep the verified release zip plus `SHA256SUMS.txt` and `release-assets.json` in the extracted folder's parent directory while you run the package-local installer, or explicitly provide `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`.
4. Extract the package.
5. Run `run.bat` from the extracted folder.

`run.bat` requests elevation when the current shell is not already elevated and then launches the packaged `bin/install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.

## Choose your path

| I want to... | Start here |
| --- | --- |
| Read the full guide in Traditional Chinese | [繁體中文文件](zh-tw/index.md) |
| Install and verify a first session quickly | [5-Minute Setup](quickstart/index.md) |
| Compare AI client registration options | [AI Agent Clients](quickstart/ai-agent-clients.md) |
| Use the server from Claude Code | [Claude Code setup](quickstart/claude-code.md) |
| Use the server from OpenAI Codex or Codex CLI | [OpenAI Codex and Codex CLI setup](quickstart/openai-codex.md) |
| Use the server from Claude Desktop | [Claude Desktop setup](quickstart/claude-desktop.md) |
| Use the server from Cursor | [Cursor setup](quickstart/cursor-vscode.md) |
| Use the server from VS Code or Visual Studio | [VS Code and Visual Studio setup](quickstart/cursor-vscode.md) |
| Understand agent-safe workflows and response contracts | [AI Agent Guide](guides/ai-agent-guide.md) |
| Review deployment and package layout contracts | [Deployment Guide](production/deployment.md) |
| Understand runtime and injection constraints | [Bootstrap and Injection](production/bootstrap-and-injection.md) |
| Contribute code, tests, or docs | [Contributor Guide](contributors/index.md) |

## Why this server is different

- **WPF-native visibility**: inspect `BindingOperations`, dependency property sources, namescopes, templates, routed events, and layout state that out-of-process tools cannot reach.
- **Agent-oriented contracts**: tool metadata lives in code, scene-first workflows are documented explicitly, and runtime follow-up guidance is returned through `navigation` and compatibility `nextSteps`.
- **Production-grade diagnostics**: the current surface includes compact binding triage, state snapshots, sequential batch mutation, buffered runtime-event draining, and scene-level summaries.
- **Hardened packaging**: the repository ships release packaging, installer generation, default-hardened injection transport, and validation steps suitable for public distribution.

## What you can do today

- Discover running WPF processes and connect to the correct target.
- Start with directly executable scene-level tools such as `get_ui_summary` and `get_form_summary`; use `get_element_snapshot(elementId)` after discovering a concrete elementId.
- Diagnose binding issues with `get_binding_errors`, `get_affected_elements`, `get_bindings`, and `get_binding_value_chain`.
- Investigate dependency property precedence, metadata, watches, and time-bounded waits.
- Run safe runtime workflows with `capture_state_snapshot`, `get_state_diff`, `restore_state_snapshot`, and `batch_mutate`.
- Trace or drain runtime event buffers with `trace_routed_events` and `drain_events`.

## Scope and boundaries

- **Transport**: the shipping server uses STDIO MCP transport.
- **Platform**: Windows only.
- **Target UI stack**: WPF only.
- **Injection model**: native bootstrapper plus managed inspector.
- **Persistence**: runtime mutations do not write back to XAML.
- **Security posture**: injection-based `connect` sessions use a persisted local HMAC secret and TLS over named pipes by default. Deterministic overrides remain available through `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR`; SDK-host reuse requires both values to match. Debug and Release builds differ in DLL validation policy.

## Architecture at a glance

```text
AI Client (Claude Code / Codex / Claude Desktop / Cursor / VS Code)
  -> MCP over STDIO
MCP Server (net8.0)
  -> named pipes with JSON messages and length-prefix framing
Native bootstrapper + managed inspector
  -> WPF Dispatcher and in-process APIs
Target WPF application
```

See [Architecture Overview](architecture/overview.md) for the full data flow and [ADR Index](architecture/adrs/index.md) for the design decisions behind the current implementation.

## Recommended reading order

1. [5-Minute Setup](quickstart/index.md)
2. [AI Agent Clients](quickstart/ai-agent-clients.md)
3. [AI Agent Guide](guides/ai-agent-guide.md)
4. [Tool Reference Overview](reference/tools/index.md)
5. [Deployment Guide](production/deployment.md)
6. [Bootstrap and Injection](production/bootstrap-and-injection.md)
