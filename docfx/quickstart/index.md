# 5-Minute Setup

This guide is optimized for the shortest successful path: build the managed server, build the native bootstrapper for your target architecture, run the included test WPF app, register the MCP server, and verify `get_processes`, `connect`, and `ping`.

## Prerequisites

- Windows 10 or later.
- .NET SDK 8.0 or later.
- Visual Studio 2022 or Build Tools with:
  - .NET desktop development
  - Desktop development with C++
- A WPF target process running under the same user account as the MCP server.

## Architecture rule first

`connect` succeeds only when the bootstrapper architecture matches the target process architecture.

- Use **x64** for most modern desktop WPF applications.
- Use **Win32/x86** if the target process is 32-bit.
- Use **ARM64** only when you are targeting a native ARM64 WPF application.

If you are unsure, start the server, call `get_processes`, and use the reported architecture as the source of truth.

## Step 1: Clone and restore

```powershell
git clone <your-fork-or-repo-url>
cd wpf-devtools-mcp
dotnet tool restore
```

## Step 2: Build the managed projects

For a typical x64 setup:

```powershell
dotnet build WpfDevTools.sln -c Debug -p:Platform=x64
```

> Use a `Debug` build for local development. Debug builds automatically relax DLL signature checks for trusted local paths, which makes first-run setup practical without code signing.

## Step 3: Build the native bootstrapper

For x64:

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=x64
```

For x86:

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=Win32
```

For ARM64:

```powershell
msbuild src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /p:Configuration=Debug /p:Platform=ARM64
```

The bootstrapper output is copied into `artifacts/bootstrapper/<Configuration>/<Platform>/` and is also resolved by the server through the candidate search paths.

## Step 4: Run the sample WPF app

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

Keep this process running.

## Step 5: Run the MCP server

Open a second terminal:

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server --no-build
```

The server uses STDIO transport, so do not send log output to `stdout` from wrappers around this process.

## Step 6: Register the server in your MCP client

Pick one of these guides:

- [Claude Desktop setup](claude-desktop.md)
- [Cursor and VS Code setup](cursor-vscode.md)

## Step 7: Verify the first session

Use this sequence in your MCP client:

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

A healthy first response sequence looks like this:

- `get_processes` lists `WpfDevTools.Tests.TestApp`
- `connect` returns success and confirms a session was created
- `ping` returns quickly
- `get_visual_tree` returns the root window and child elements

## Fastest useful prompt for an AI client

```text
List WPF processes, connect to the test app, ping it, and show me the top two levels of the visual tree.
```

## If `connect` fails

Check these in order:

1. The target application is WPF and still running.
2. The MCP server and target process use matching architecture.
3. The native bootstrapper for that architecture was built.
4. You are using a `Debug` build for unsigned local development, or a signed `Release` build for production.
5. The target process is not blocked by policy, antivirus, or insufficient privileges.

Next: [AI Agent Guide](../guides/ai-agent-guide.md)