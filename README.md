# WPF DevTools MCP Server

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-SDK%20v1.0-blue)](https://modelcontextprotocol.io/)
[![Tests](https://img.shields.io/badge/tests-3600%2B-brightgreen)]()
[![Coverage](https://codecov.io/gh/Evanlau1798/wpf-devtools-mcp/branch/master/graph/badge.svg)](https://codecov.io/gh/Evanlau1798/wpf-devtools-mcp)

`wpf-devtools-mcp` is a Model Context Protocol server for inspecting and interacting with running WPF applications. It bridges MCP clients to an in-process inspector so agents can query visual trees, inspect bindings, analyze dependency properties, and trigger UI interactions that out-of-process tooling cannot access.

Canonical repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
Published releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)

## Current State

- Shipping transport is STDIO only. The server is wired through `WithStdioServerTransport()`.
- The server bootstrap uses `Host.CreateEmptyApplicationBuilder(...)` so the process does not inherit default console logging that could pollute `stdout`.
- MCP clients should treat tool discovery as the source of truth for exposed input schemas, and read `wpf://contracts/response` for stable response contracts; this README stays intentionally high level and may describe runtime conventions that are not universally discoverable from every client SDK.
- HTTP/SSE work remains planned and is not part of the current server binary.
- MCP schemas improve discovery, but runtime validation still happens inside tool handlers because generated schemas and SDK annotations are not a substitute for validating untrusted arguments.
- Tool metadata is written for agent use: each tool description should state what the tool does, when to use it, when not to use it, and the limits of the returned data.

## Why This Server Exists

- WPF binding internals, dependency property precedence, and routed event graphs require in-process access.
- AI agents need compact, reliable diagnostics instead of raw Visual Tree dumps or protocol trivia.
- The project focuses on AI-friendly tool metadata, predictable workflows, and safe-by-default documentation.

## Quick Start

### Prerequisites

- Windows with the .NET runtime required by the published package
- A target WPF application running under the same user account

If you are building from source instead of using a published release, install:

- .NET SDK 8.0 or newer
- .NET Framework 4.8 targeting pack
- Visual Studio 2022 Build Tools (or the full IDE) with the **Desktop development with C++** workload, so `msbuild` can build the native `WpfDevTools.Bootstrapper.vcxproj` bootstrapper for the target architectures (x64, x86, arm64)

### Install with the reviewed online installer

For first-time setup, prefer the reviewed installer flow instead of launching from the source tree or manually expanding a release archive. The public bootstrap script downloads the selected published GitHub Release package; it does not build from source.

Preferred path on Windows:

> **Security note**: Review [scripts/online-installer.ps1](scripts/online-installer.ps1) first. The online installer resolves the versioned GitHub Release asset, validates archive integrity before extraction, and then installs the extracted packaged payload through the reviewed installer/helper flow.

```powershell
irm https://wpf-mcptools.evanlau1798.com | iex
```

That script-first path keeps `scripts/online-installer.ps1` as the canonical source entrypoint while still installing from a published release package. The default interactive flow asks for a release version, chooses the current machine architecture by default, and then asks which MCP client registration to generate.
When you omit `-Architecture`, the installer detects the system architecture (`x64`, `x86`, or `arm64`). Pass `-Architecture` only when you intentionally need to install a different package.

If you want a single-command, non-interactive setup for a specific client, use:

```powershell
& ([scriptblock]::Create((irm https://wpf-mcptools.evanlau1798.com))) -Version latest -Client claude-code -NonInteractive -Force -OutputJson
```

For local audit or offline bootstrap scenarios, you can also run the repository copy directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest
```

Manual fallback:

1. Download `release_<version>_win-x64.zip`, `release_<version>_win-x86.zip`, or `release_<version>_win-arm64.zip` from Releases together with `SHA256SUMS.txt` and `release-assets.json`.
2. Verify the downloaded archive against the release provenance sidecars before extraction. Confirm the asset hash matches `SHA256SUMS.txt` and the exact asset entry in `release-assets.json`. The reviewed online installer performs this verification automatically; the manual path does not.
3. Keep the verified release zip plus `SHA256SUMS.txt` and `release-assets.json` in the extracted package's parent directory while you run the package-local installer, or explicitly provide `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`.
4. Extract the archive.
5. Run `run.bat` from the extracted package. It requests elevation when the current shell is not already elevated and launches the packaged `bin\install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.
  For `claude-code` and `codex`, elevated PATH-based CLI discovery is intentionally blocked. Use `WPFDEVTOOLS_SKIP_ELEVATION=1` for the CLI registration step, register manually after install, or provide a trusted absolute path with `WPFDEVTOOLS_CLAUDE_COMMAND_PATH` or `WPFDEVTOOLS_CODEX_COMMAND_PATH`.
6. Register or verify the installed executable in your MCP client.

The installer writes ready-to-copy registration snippets under `<InstallRoot>\<arch>\client-registration\`. If you do not pass `-InstallRoot`, the installer reuses the last live install root when possible; otherwise it falls back to `%APPDATA%\WpfDevToolsMcp`.

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

> **Security note**: By default, injection-based inspector sessions use a persisted local HMAC secret and TLS over named pipes.
> Set `WPFDEVTOOLS_AUTH_SECRET` when you need a deterministic shared secret, and set `WPFDEVTOOLS_CERT_DIR` when you need to pin certificate storage to a specific directory.
> `WPFDEVTOOLS_CERT_DIR` must be an absolute path when you set it explicitly.
> For `connect()` to reuse an SDK-hosted Inspector, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` together on both sides before calling `InspectorSdk.Initialize()`. The default-hardened MCP server will not reuse a plaintext SDK host.
> If either value is missing, or both are unset, `InspectorSdk.Initialize()` now fails closed instead of starting a plaintext SDK host. Legacy plaintext or otherwise unresponsive existing hosts can still surface as timeout during reuse detection.
> `connect()` can also reuse an existing SDK-hosted Inspector when the target app calls `InspectorSdk.Initialize()` with matching transport settings, including the same absolute `WPFDEVTOOLS_CERT_DIR` value.
> Raw DLL injection into same-user WPF processes is blocked by default, including project-scoped targets under the current repository root.
> `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` accepts a semicolon-separated list of exact absolute executable paths. Prefer the SDK-hosted reuse path first, and use the allowlist only when raw injection into a specific target is an intentional production decision.
> When `connect()` blocks raw injection, it returns `errorCode: SecurityError` together with `requiresExplicitTargetOptIn: true` so clients can distinguish this policy boundary from packaging or transport failures.
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
5. After diagnostics, interaction, or mutation, prefer `navigation.recommended` first and treat `nextSteps` as the compatibility field for older clients instead of guessing the next tool.

## Maintainer Release Flow

Before creating or publishing a GitHub Release, run the local no-upload preflight command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/tools/packaging/Preflight-Release.ps1 -VersionTag v0.1.0 -OutputJson
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
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

### VS Code

Use the installer-generated `client-registration\vscode.json` artifact as the source of truth for the resolved executable path. By default, installer-managed VS Code registrations go to `%APPDATA%\Code\User\mcp.json`; use `.vscode\mcp.json` only when you intentionally want a manual project-scoped alternative.

Example project-scoped configuration:

```json
{
  "servers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

### Cursor

Use the installer-generated `client-registration\cursor.global.json` or `client-registration\cursor.project.json` artifact as the source of truth for the resolved executable path. By default, installer-managed global Cursor registrations go to `%USERPROFILE%\.cursor\mcp.json`; use `.cursor\mcp.json` only when you intentionally want a manual project-scoped alternative.

Example project-scoped configuration:

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

### General Notes

- Register this server as a local STDIO command.
- Keep all logs off `stdout`; the server already routes operational logs to stderr and file output.
- Use MCP tool discovery for input schemas; do not hard-code request shapes in prompts or scripts.
- Use `wpf://contracts/response` for machine-readable response contracts, including `structuredContent`, `navigation`, and `nextSteps`.

## Important Contract Notes

- `element_screenshot` defaults to `outputMode: "metadata"` and also supports `"file"` or explicit `"base64"`. Metadata responses include dimensions, `format`, `rendered: false`, and `byteLength: 0` without rendering PNG bytes. File/base64 responses render pixels, include `rendered: true`, dimensions, `format`, and `byteLength`; file mode also returns `screenshotId`, `path`, and `sha256`, while base64 mode returns `base64Image`.
- `set_dp_value`, `modify_viewmodel`, and `override_style_setter` accept raw JSON values, not string-only payloads.
- Inspector-originated failures may also return `errorCode` and optional `errorData` alongside `error`.
- `compare_trees` accepts an optional `elementId`.
- `force_binding_update` accepts an optional `direction`.
- `drag_and_drop` accepts an optional `dataFormat`.
- `simulate_keyboard` accepts an optional `eventType`.
- `fire_routed_event` accepts optional JSON `eventArgs`.

## Security

The current implementation hardens the default injection-based transport and still supports explicit overrides through environment variables.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Overrides the generated HMAC challenge-response secret | Use a base64 secret when production deployments require deterministic credential rotation |
| `WPFDEVTOOLS_CERT_DIR` | Overrides the default TLS certificate directory for named pipes | Use a private directory per environment when certificate storage must be pinned |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected inspector certificate | Useful when you want strict certificate selection |
| `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` | Explicitly allowlists raw-injection targets | Use a semicolon-separated list of exact absolute executable paths when SDK-hosted reuse is not possible |
| `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` | Restricts all `connect()` targets | Required semicolon-separated exact absolute executable paths; unset or malformed configured entries fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` | Gates runtime mutation, interaction, and render-measurement tools | Set `true` to opt in; unset, invalid, or `false` values fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` | Gates `element_screenshot` | Set `true` to opt in; unset, invalid, or `false` values fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` | Gates ViewModel inspection tools | Set `true` to opt in; unset, invalid, or `false` values fail closed |

Security deployment guidance lives in `SECURITY.md`.

## Tool Categories

The server ships 64 MCP tools across 11 categories. Use MCP tool discovery for input schemas.

<details>
<summary>Tool category overview</summary>

| Category | Tools | Description |
|----------|-------|-------------|
| Process Management | 5 | `connect`, `get_processes`, `ping`, `get_active_process`, `select_active_process` |
| Tree & XAML | 8 | `get_visual_tree`, `get_logical_tree`, `compare_trees`, `serialize_to_xaml`, `get_namescope`, `get_template_tree`, `get_windows`, `find_elements` |
| Binding Diagnostics | 7 | `get_bindings`, `get_affected_elements`, `get_binding_errors`, `get_binding_value_chain`, `get_binding_mismatches`, `get_datacontext_chain`, `force_binding_update` |
| DependencyProperty | 7 | `get_dp_value_source`, `get_dp_metadata`, `set_dp_value`, `clear_dp_value`, `watch_dp_changes`, `wait_for_dp_change`, `wait_for_dp_change_after_mutation` |
| Style/Template | 4 | `get_applied_styles`, `get_triggers`, `get_resource_chain`, `override_style_setter` |
| RoutedEvent | 4 | `trace_routed_events`, `get_event_handlers`, `fire_routed_event`, `drain_events` |
| Interaction | 7 | `click_element`, `drag_and_drop`, `get_focus_state`, `focus_element`, `scroll_to_element`, `simulate_keyboard`, `element_screenshot` |
| Layout | 4 | `get_layout_info`, `get_clipping_info`, `highlight_element`, `invalidate_layout` |
| MVVM | 5 | `get_viewmodel`, `get_commands`, `execute_command`, `modify_viewmodel`, `get_validation_errors` |
| Performance | 4 | `get_render_stats`, `find_binding_leaks`, `measure_element_render_time`, `get_visual_count` |
| State & Scene Diagnostics | 9 | `capture_state_snapshot`, `restore_state_snapshot`, `batch_mutate`, `get_state_diff`, `get_element_snapshot`, `diagnose_visibility`, `get_interaction_readiness`, `get_ui_summary`, `get_form_summary` |

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
- [Anthropic tool-definition guidance](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools)
- [Anthropic tool design guidance](https://www.anthropic.com/engineering/writing-tools-for-agents)

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
    | Direct WPF Runtime Access
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
- Structured content: `StructuredContent` is populated on all tool results; object/array `Content.Text` preserves high-signal top-level scalar fields and collection counts as a compact fallback summary when structured payload is present. Set `WPFDEVTOOLS_TEXT_FALLBACK_MODE=full` only for legacy text-only MCP clients that require the full JSON payload in `content[0].text`; error results include `Annotations`.
- README tool catalog: intentionally minimized to prevent schema drift
