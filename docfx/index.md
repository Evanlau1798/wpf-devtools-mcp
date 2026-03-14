# WPF DevTools MCP Server

Looking for Traditional Chinese docs? Start at [繁體中文文件](zh-tw/index.md).

WPF DevTools MCP Server is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications through an injected in-process inspector. It is designed for scenarios where UI Automation is not enough: binding diagnostics, dependency property precedence, scene-level summaries, MVVM inspection, routed-event tracing, layout debugging, and controlled runtime mutation.

## Canonical sources

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)
- Online installer source: [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)
- Release packaging source: [scripts/release/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Publish-Release.ps1)
- Installed-layout source: [scripts/release/Install-WpfDevTools.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Install-WpfDevTools.ps1)

`scripts/` is the canonical source of truth for installer and release behavior. This DocFX site documents those scripts; it does not define them.

## Install paths

### Online installer path

Review the canonical source first: [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)

Default one-line install:

```powershell
irm https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1 | iex
```

Client-specific example:

```powershell
& ([scriptblock]::Create((irm 'https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1'))) -Version latest -Architecture x64 -Client claude-code -Force
```

### Manual release package path

1. Download `release_<version>_win-x64.zip`, `release_<version>_win-x86.zip`, or `release_<version>_win-arm64.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Extract the package.
3. Run `setup.ps1 -Force` from the extracted folder.

## Choose your path

| I want to... | Start here |
| --- | --- |
| Read the full guide in Traditional Chinese | [繁體中文文件](zh-tw/index.md) |
| Install and verify a first session quickly | [5-Minute Setup](quickstart/index.md) |
| Compare AI client registration options | [AI Agent Clients](quickstart/ai-agent-clients.md) |
| Use the server from Claude Code | [Claude Code setup](quickstart/claude-code.md) |
| Use the server from OpenAI Codex or Codex CLI | [OpenAI Codex and Codex CLI setup](quickstart/openai-codex.md) |
| Use the server from Claude Desktop | [Claude Desktop setup](quickstart/claude-desktop.md) |
| Use the server from Cursor or VS Code | [Cursor and VS Code setup](quickstart/cursor-vscode.md) |
| Understand agent-safe workflows and response contracts | [AI Agent Guide](guides/ai-agent-guide.md) |
| Review deployment and package layout contracts | [Deployment Guide](production/deployment.md) |
| Understand runtime and injection constraints | [Bootstrap and Injection](production/bootstrap-and-injection.md) |
| Contribute code, tests, or docs | [Contributor Guide](contributors/index.md) |

## Why this server is different

- **WPF-native visibility**: inspect `BindingOperations`, dependency property sources, namescopes, templates, routed events, and layout state that out-of-process tools cannot reach.
- **Agent-oriented contracts**: tool metadata lives in code, scene-first workflows are documented explicitly, and runtime follow-up guidance is returned through `navigation` and compatibility `nextSteps`.
- **Production-grade diagnostics**: the current surface includes compact binding triage, state snapshots, sequential batch mutation, buffered runtime-event draining, and scene-level summaries.
- **Hardened packaging**: the repository ships release packaging, installer generation, optional security controls, and validation steps suitable for public distribution.

## What you can do today

- Discover running WPF processes and connect to the correct target.
- Start with scene-level tools such as `get_ui_summary`, `get_element_snapshot`, and `get_form_summary`.
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
- **Security posture**: authentication and TLS are opt-in; Debug and Release builds differ in DLL validation policy.

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
