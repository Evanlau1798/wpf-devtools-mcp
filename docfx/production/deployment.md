# Deployment Guide

## Deployment model

This server is usually deployed as a local Windows companion process next to the target WPF application.

## Canonical script sources

Installer and packaging behavior are defined in `scripts/`, not in the documentation site:

- [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)
- [scripts/release/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Publish-Release.ps1)
- [scripts/release/Install-WpfDevTools.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Install-WpfDevTools.ps1)

## Recommended install modes

### Public release package

1. Download the architecture-matched `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Extract the package.
3. Run `setup.ps1 -Force`.

### Script-driven install

Review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) first. Then choose either remote or local execution.

Remote example:

```powershell
irm https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1 | iex
```

Client-specific remote example:

```powershell
& ([scriptblock]::Create((irm 'https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1'))) -Version latest -Architecture x64 -Client claude-code -Force
```

Local example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -Force
```

## Remote script execution is optional

Any remote `irm | iex` flow is optional. Review the repository source first and treat the script in `scripts/` as the authoritative implementation.

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
- Keep the published `bin/inspectors` and `bin/bootstrapper` folders next to the installed server layout.
- Sign Release inspector binaries.
- Configure authentication and TLS settings for hardened environments.
- Validate `get_processes`, `connect`, and a scene-level call such as `get_ui_summary` from the installed path outside the repository.
