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