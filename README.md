# WPF DevTools MCP Server

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

### Build

For most users, prefer a published release instead of running directly from the source tree. Use the source-based steps below when you are debugging locally or contributing to the repository.

```powershell
dotnet build WpfDevTools.sln -c Debug -p:Platform=x64
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=x64
```
> **Build note**: The repository ships `Directory.Build.props` and `Directory.Build.rsp` that disable shared compilation and MSBuild node reuse to prevent file-lock warnings (MSB3026). These settings are applied automatically - no extra flags are needed.
> **Public setup note**: Prefer a published release for first-time setup so the server, Inspector DLLs, and native bootstrapper stay in the documented release layout.

### Run the server

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server/
```

> **Security note**: By default the server runs without authentication or encryption.
> For production use, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR`.
> See [SECURITY.md](SECURITY.md) for details.
> **Local development note**: Use a `Debug` build for local development and build the native bootstrapper for the same architecture as the target process.
> Debug builds automatically skip DLL signature verification for trusted local DLL paths, allowing unsigned local development.
> `connect` still requires architecture-compatible bootstrapper and injector binaries (for example x64 target -> x64 build, x86 target -> Win32/x86 build).`
> The server process architecture must match the target process (for example x86 target -> x86 server process, x64 target -> x64 server process).

### Typical MCP workflow

1. Call `get_processes` to discover WPF targets.
2. Call `connect(processId)` before any process-specific tool.
3. Use tree tools first (`get_visual_tree`, `get_logical_tree`) to obtain `elementId` values.
4. Move to diagnostics (`get_bindings`, `get_binding_errors`, `get_dp_value_source`) or interaction tools (`click_element`, `simulate_keyboard`, `element_screenshot`).

## MCP Client Configuration

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\src\\WpfDevTools.Mcp.Server", "--no-build"]
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
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\src\\WpfDevTools.Mcp.Server", "--no-build"]
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

## Documentation Map

- `docfx/` -> public DocFX site source for GitHub Pages
- `docfx/zh-tw/` -> Traditional Chinese public documentation mirror
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
- Structured content: `StructuredContent` and `Annotations` populated on all tool results
- README tool catalog: intentionally minimized to prevent schema drift
