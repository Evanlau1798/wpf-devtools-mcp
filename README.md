# WPF DevTools MCP Server

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-SDK%20v1.0-blue)](https://modelcontextprotocol.io/)
[![Tests](https://img.shields.io/badge/tests-1300%2B-brightgreen)]()
[![Coverage](https://img.shields.io/badge/coverage-83%25-brightgreen)]()

`wpf-devtools-mcp` is a Model Context Protocol server for inspecting and interacting with running WPF applications. It bridges MCP clients to an in-process inspector so agents can query visual trees, inspect bindings, analyze dependency properties, and trigger UI interactions that out-of-process tooling cannot access.

## Current State

- Shipping transport is STDIO only. The server is wired through `WithStdioServerTransport()`.
- The server bootstrap uses `Host.CreateEmptyApplicationBuilder(...)` so the process does not inherit default console logging that could pollute `stdout`.
- MCP clients should treat tool discovery as the source of truth for detailed schemas; this README stays intentionally high level.
- HTTP/SSE work remains planned and is not part of the current server binary.

## Why This Server Exists

- WPF binding internals, dependency property precedence, and routed event graphs require in-process access.
- AI agents need compact, reliable diagnostics instead of raw Visual Tree dumps or protocol trivia.
- The project focuses on AI-friendly tool metadata, predictable workflows, and safe-by-default documentation.

## Quick Start

### Prerequisites

- Windows with .NET SDK 8.0+
- A target WPF application running under the same user account

### Install from a published release

For first-time setup, prefer the published release flow instead of launching from the source tree.

Fastest path on Windows:

> **Security note**: This command downloads and executes a remote script. Review the [script source](scripts/online-installer.ps1) before running in sensitive environments.

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

That GitHub Pages hosted bootstrap script downloads the matching `release_<version>_win-<arch>.zip` package from GitHub Releases and runs the packaged `setup.ps1` wizard.
Maintainers should treat `scripts/online-installer.ps1` as the canonical source entrypoint for that hosted installer flow.

If you want a single-command, non-interactive setup for a specific client and architecture, use:

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code -NonInteractive -Force
```

Manual fallback:

1. Download `release_<version>_win-x64.zip`, `release_<version>_win-x86.zip`, or `release_<version>_win-arm64.zip` from Releases.
2. Extract the archive.
3. Run `setup.ps1 -Force` from the extracted package. Use `install.ps1` only when you want the lower-level copy/install flow without the interactive setup wizard.
4. Register or verify the installed executable in your MCP client.

The installer writes ready-to-copy registration snippets under `%LOCALAPPDATA%\WpfDevToolsMcp\<arch>\client-registration\`.

### Build

For most users, prefer a published release instead of running directly from the source tree. Use the source-based steps below when you are debugging locally or contributing to the repository.

```powershell
dotnet build WpfDevTools.sln -c Debug -p:Platform=x64
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=x64
```
> **Build note**: The repository ships `Directory.Build.props` and `Directory.Build.rsp` that disable shared compilation and MSBuild node reuse to prevent file-lock warnings (MSB3026). These settings are applied automatically - no extra flags are needed.
> **Rebuild note**: Close running `WpfDevTools.Mcp.Server` and test-app processes before broad rebuilds, or MSBuild can still hit locked output files.
> **Public setup note**: Prefer a published release for first-time setup so the server, Inspector DLLs, and native bootstrapper stay in the documented release layout.

### Run the server

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server/
```

> **Security note**: By default the server runs without authentication or encryption.
> For production use, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR`.
> See [SECURITY.md](SECURITY.md) for details.
> **Local development note**: Use a `Debug` build for local development and build the native bootstrapper for the same architecture as the target process.
> Debug builds skip DLL signature verification only for trusted local DLL paths under the configured trusted-root policy, allowing unsigned local development without weakening public release guidance.
> `connect` still requires architecture-compatible bootstrapper and injector binaries (for example x64 target -> x64 build, x86 target -> Win32/x86 build).
> The server process architecture must match the target process (for example x86 target -> x86 server process, x64 target -> x64 server process).

### Typical MCP workflow

1. Call `connect()` first and let the server auto-discover the target when only one visible WPF app is available.
2. Call `get_processes(windowFilter)` only when `connect()` reports multiple candidates or when you intentionally need background / foreground filtering before connecting.
3. Build initial context with `get_ui_summary`, `get_element_snapshot`, or `get_form_summary` before expanding full trees.
4. Use tree tools only when scene-level summaries are insufficient and you need stable `elementId` values.
5. After diagnostics, interaction, or mutation, prefer the returned `nextSteps` / `navigation` guidance over ad hoc tool guessing.

## Maintainer Release Flow

Before creating or publishing a GitHub Release, run the local no-upload preflight command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Preflight-Release.ps1 -VersionTag v0.1.0 -OutputJson
```

This validates the Release build, unit tests, package layout, and staged GitHub Release assets without uploading to GitHub.
For the full maintainer checklist and workflow details, see [RELEASING.md](RELEASING.md).

Maintainer prerequisites for packaged releases:

- Install Visual Studio 2022 or Build Tools 2022 with `Desktop development with C++`.
- Ensure the native toolchain includes MSVC `v143` build tools for `x64`, `x86`, and `ARM64`, because the bootstrapper package is built per target architecture.
- If local preflight fails only on `ARM64`, the repository is usually fine; the maintainer machine is missing the ARM64 native build components needed by `WpfDevTools.Bootstrapper.vcxproj`.

## MCP Client Configuration

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "command": "%LOCALAPPDATA%\\WpfDevToolsMcp\\x64\\current\\WpfDevTools.Mcp.Server.exe",
      "args": []
    }
  }
}
```

### VS Code / Cursor

Add to `.vscode/mcp.json`:

```json
{
  "servers": {
    "wpf-devtools": {
      "command": "%LOCALAPPDATA%\\WpfDevToolsMcp\\x64\\current\\WpfDevTools.Mcp.Server.exe",
      "args": []
    }
  }
}
```

### General Notes

- Register this server as a local STDIO command.
- Keep all logs off `stdout`; the server already routes operational logs to stderr and file output.
- Prefer MCP tool discovery instead of hard-coding request shapes in prompts or scripts.

## Important Contract Notes

- `element_screenshot` returns `base64Image`, `width`, `height`, and `format`.
- `set_dp_value`, `modify_viewmodel`, and `override_style_setter` accept raw JSON values, not string-only payloads.
- Inspector-originated failures may also return `errorCode` and optional `errorData` alongside `error`.
- `compare_trees` accepts an optional `elementId`.
- `force_binding_update` accepts an optional `direction`.
- `drag_and_drop` accepts an optional `dataFormat`.
- `simulate_keyboard` accepts an optional `eventType`.
- `fire_routed_event` accepts optional JSON `eventArgs`.

## Security

The current implementation supports opt-in transport hardening through environment variables.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Enables HMAC challenge-response authentication | Use a base64 secret in production |
| `WPFDEVTOOLS_CERT_DIR` | Enables TLS over named pipes using a local certificate store | Use a private directory per environment |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected inspector certificate | Useful when you want strict certificate selection |

Security deployment guidance lives in `SECURITY.md`.

## Tool Categories

The server ships 62 MCP tools across 11 categories. Use MCP tool discovery for full schemas.

<details>
<summary>Tool category overview</summary>

| Category | Tools | Description |
|----------|-------|-------------|
| Process Management | 5 | `connect`, `get_processes`, `ping`, `get_active_process`, `select_active_process` |
| Tree & XAML | 8 | `get_visual_tree`, `get_logical_tree`, `compare_trees`, `serialize_to_xaml`, `get_namescope`, `get_template_tree`, `get_windows`, `find_elements` |
| Binding Diagnostics | 7 | `get_bindings`, `get_affected_elements`, `get_binding_errors`, `get_binding_value_chain`, `get_binding_mismatches`, `get_datacontext_chain`, `force_binding_update` |
| DependencyProperty | 6 | `get_dp_value_source`, `get_dp_metadata`, `set_dp_value`, `clear_dp_value`, `watch_dp_changes`, `wait_for_dp_change` |
| Style/Template | 4 | `get_applied_styles`, `get_triggers`, `get_resource_chain`, `override_style_setter` |
| RoutedEvent | 4 | `trace_routed_events`, `get_event_handlers`, `fire_routed_event`, `drain_events` |
| Interaction | 7 | `click_element`, `drag_and_drop`, `get_focus_state`, `focus_element`, `scroll_to_element`, `simulate_keyboard`, `element_screenshot` |
| Layout | 4 | `get_layout_info`, `get_clipping_info`, `highlight_element`, `invalidate_layout` |
| MVVM | 5 | `get_viewmodel`, `get_commands`, `execute_command`, `modify_viewmodel`, `get_validation_errors` |
| Performance | 4 | `get_render_stats`, `find_binding_leaks`, `measure_element_render_time`, `get_visual_count` |
| State & Scene Diagnostics | 8 | `capture_state_snapshot`, `restore_state_snapshot`, `get_state_diff`, `get_element_snapshot`, `diagnose_visibility`, `get_interaction_readiness`, `get_ui_summary`, `get_form_summary` |

</details>

## Documentation Map

- `CONTRIBUTING.md` -> contribution guidelines with TDD requirements
- `docfx/` -> public DocFX site source for GitHub Pages
- `docfx/zh-tw/` -> Traditional Chinese public documentation mirror
- `RELEASING.md` -> maintainer-facing release checklist, workflow, and local preflight guide
- `SECURITY.md` -> supported security settings and deployment guidance
- `EXAMPLES.md` -> usage examples for common tools and workflows
- Detailed MCP tool metadata is maintained in code through the official SDK attributes to reduce README drift.

## Official References

- [MCP build-server guide (C#)](https://modelcontextprotocol.io/docs/develop/build-server#c)
- [ModelContextProtocol C# SDK API](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.html)

## Development Notes

- Production code and tests aim to stay below 500 lines per file.
- New features and fixes are expected to follow TDD: failing test first, minimal implementation second, refactor third.
- WPF-specific STA constraints are documented in the repository instructions and test suite conventions.

## Architecture

```text
AI Agent (Claude Code / Cursor / etc.)
    | MCP Protocol (STDIO)
MCP Server (Tool Router, Session Manager)
    | Named Pipes IPC (0.1-1ms latency)
Injected Inspector DLL (In-Process)
    | Direct Memory Access
Target WPF Application
```

DLL injection uses the `CreateRemoteThread` technique (Snoop-based). Once injected, the Inspector DLL runs in the target process and communicates with the MCP Server over Named Pipes using JSON-RPC with length-prefix framing.

## Repository Layout

```text
src/
  WpfDevTools.Mcp.Server/   # MCP Server (STDIO transport, tool routing, session management)
  WpfDevTools.Inspector/     # Injected DLL (Visual Tree, Binding, DP analyzers)
  WpfDevTools.Inspector.Sdk/ # Opt-in SDK for non-injection integration scenarios
  WpfDevTools.Injector/      # Process injection (CreateRemoteThread)
  WpfDevTools.Bootstrapper/  # Native bootstrapper DLL loaded before managed inspector startup
  WpfDevTools.Shared/        # Shared types, security, IPC protocol
tests/
  WpfDevTools.Tests.Unit/
  WpfDevTools.Tests.Integration/
  WpfDevTools.Tests.TestApp/  # Test WPF application with intentional binding errors
```

## License

MIT. DLL injection code includes Snoop-based components under Ms-PL attribution.

## Status Summary

- Official C# SDK: in use
- STDIO transport: in use
- HTTP transport: planned
- Tool metadata: maintained in code
- Structured content: `StructuredContent` is populated on all tool results, and error results include `Annotations`.
- README tool catalog: intentionally minimized to prevent schema drift
