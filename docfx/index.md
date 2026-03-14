# WPF DevTools MCP Server

Looking for Traditional Chinese docs? Start at [繁體中文文件](zh-tw/index.md).

WPF DevTools MCP Server is a Windows-only Model Context Protocol server for inspecting and interacting with running WPF applications through an injected in-process inspector. It exists for scenarios where UI Automation is not enough: binding diagnostics, dependency property precedence, visual tree introspection, MVVM analysis, routed events, layout debugging, and targeted UI interactions.

## Install in one command

For most users, the fastest supported setup path is the GitHub Pages bootstrap installer:

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

> Security note: Review the hosted installer script before using `irm | iex` in sensitive environments.

That script downloads the matching `WpfDevTools-win-<arch>.zip` asset from GitHub Releases and runs the packaged `setup.ps1` wizard.

If you do not want `irm | iex`, download the release zip manually, inspect it, and run `setup.ps1 -Force` locally.

If you want a deterministic non-interactive install, use:

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code -NonInteractive -Force
```

Repository and Releases:

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)

## Choose your path

| I want to... | Start here |
| --- | --- |
| Read the full guide in Traditional Chinese | [繁體中文文件](zh-tw/index.md) |
| Be up and running in under five minutes | [5-Minute Setup](quickstart/index.md) |
| Use the server from Claude Code | [Claude Code setup](quickstart/claude-code.md) |
| Use the server from OpenAI Codex or Codex CLI | [OpenAI Codex and Codex CLI setup](quickstart/openai-codex.md) |
| Compare all AI client options first | [AI Agent Clients](quickstart/ai-agent-clients.md) |
| Use the server from Claude Desktop | [Claude Desktop setup](quickstart/claude-desktop.md) |
| Use the server from Cursor or VS Code | [Cursor and VS Code setup](quickstart/cursor-vscode.md) |
| Understand how AI agents should use the tools safely | [AI Agent Guide](guides/ai-agent-guide.md) |
| Deploy with production-grade security controls | [Security Model](production/security.md) |
| Understand runtime, bootstrapper, and architecture constraints | [Bootstrap and Injection](production/bootstrap-and-injection.md) |
| Contribute code, tests, or docs | [Contributor Guide](contributors/index.md) |

## What makes this server different

- **WPF-native visibility**: inspect `BindingOperations`, dependency property sources, namescopes, templates, routed events, and layout details that out-of-process tools cannot access.
- **AI-friendly contracts**: tool metadata is maintained in code, structured content is returned consistently, and common automation workflows are documented explicitly.
- **Production hardening**: the current codebase includes DLL validation, optional HMAC authentication, optional TLS over named pipes, pipe ACL restrictions, and bounded request handling.
- **Verified workflows**: the repository includes unit tests, integration tests, and a live MCP smoke harness that exercises all shipped tools against the test application.

## What you can do today

- Discover running WPF processes and connect to a target application.
- Browse the visual tree, logical tree, namescope, and template tree.
- Diagnose binding errors, inspect binding chains, and refresh bindings.
- Investigate dependency property values, metadata, style setters, and resource lookup.
- Trigger safe interactions such as clicking, scrolling, keyboard simulation, screenshots, and controlled drag/drop.
- Inspect layout, clipping, routed events, MVVM commands, and performance diagnostics.

## Current scope and boundaries

- **Transport**: the shipping server uses STDIO for MCP transport.
- **Platform**: Windows only.
- **Target UI stack**: WPF applications only.
- **Injection model**: native bootstrapper + managed inspector.
- **Security posture**: authentication and TLS are opt-in; debug and release builds behave differently during DLL validation.

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

See [Architecture Overview](architecture/overview.md) for the full data flow and [ADR Index](architecture/adrs/index.md) for the design decisions that shaped the current implementation.

## Recommended reading order

1. [5-Minute Setup](quickstart/index.md)
2. [AI Agent Clients](quickstart/ai-agent-clients.md)
3. [AI Agent Guide](guides/ai-agent-guide.md)
4. [Tool Reference Overview](reference/tools/index.md)
5. [Security Model](production/security.md)
6. [Bootstrap and Injection](production/bootstrap-and-injection.md)
