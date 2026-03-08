# Deployment Guide

## Deployment model

This server is usually deployed as a local Windows companion process next to the target WPF application.

## Recommended install modes

### GitHub Pages bootstrap installer

The public fast path is the static GitHub Pages bootstrap script:

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

That script downloads the architecture-matched `WpfDevTools-win-<arch>.zip` asset from GitHub Releases and then runs the packaged `setup.ps1` installer.

### Offline or reviewed install

If you do not want `irm | iex`, download the release zip manually, inspect it, extract it, and run `setup.ps1 -Force` locally.

## `irm | iex` is optional, not a trust boundary

The `irm | iex` path exists for fast setup, but it is optional and should not be your only trust model. It is also not a SmartScreen bypass. SmartScreen reputation and code signing remain separate concerns.

## Release layout matters

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
