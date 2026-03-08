# Deployment Guide

## Deployment model

This server is typically deployed as a local developer or test-automation companion process on the same Windows machine as the target WPF application.

The current transport is STDIO. That means deployment usually looks like this:

- a local MCP-capable client launches `WpfDevTools.Mcp.Server`
- the server discovers and connects to a local WPF process
- the server injects the bootstrapper and inspector into that target process
- the server and inspector communicate over named pipes

## Production deployment checklist

- Build the server in `Release`.
- Build the native bootstrapper for every architecture you intend to support.
- Sign the inspector DLL for release use.
- Configure authentication and TLS environment variables.
- Verify the server can reach the target app under the same user or required privilege boundary.
- Smoke-test `get_processes`, `connect`, `ping`, and one representative inspection workflow.

## Development command vs release command

The quickstart guides use a development-time command from the repository root:

```powershell
dotnet run --project src/WpfDevTools.Mcp.Server --no-build
```

For production or a reusable internal bundle, publish first and launch the built output instead:

```powershell
dotnet publish src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj -c Release -o <PUBLISH_DIR>
dotnet "<PUBLISH_DIR>\WpfDevTools.Mcp.Server.dll"
```

When registering the production server in an MCP client, point the client at the published output rather than the source project command. Example:

```powershell
claude mcp add --transport stdio wpf-devtools -- dotnet "<PUBLISH_DIR>\WpfDevTools.Mcp.Server.dll"
```

## Minimal release artifact set

A practical release bundle should include:

- MCP server binaries
- inspector binaries
- architecture-specific native bootstrapper binaries
- any required runtime dependencies
- deployment notes for environment variables and architecture selection

## Operational guidance

- Keep operational logs on stderr or file output, never on stdout.
- Publish architecture-specific release notes if you distribute x86, x64, and ARM64 variants.
- Test one real target app per supported runtime family before release: .NET Framework and modern .NET.
