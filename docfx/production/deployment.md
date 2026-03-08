# Deployment Guide

## Deployment model

This server is usually deployed as a local Windows companion process next to the target WPF application.

## Recommended install modes

### Safe default

Download the release package or installer script, inspect it, and run it locally.

### Convenience mode

The `irm | iex` path exists for fast setup, but it is optional and should not be your only trust model.

## release layout matters

The bootstrapper and inspector sidecars are discovered relative to the server, so the documented release layout must stay stable across installs and upgrades.

See [Release Layout](release-layout.md) for the exact folder contract.

## Installed executable contract

The MCP client should launch the installed `WpfDevTools.Mcp.Server.exe`, for example:

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## Production checklist

- Use the architecture that matches the target process.
- Keep the published `inspectors` and `bootstrapper` folders next to the installed server layout.
- Sign release inspector binaries.
- Configure authentication and TLS settings for hardened environments.
- Validate `get_processes`, `connect`, and `ping` from the installed path outside the repository.
