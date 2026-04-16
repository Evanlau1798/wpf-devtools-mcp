# Deployment Guide

## Deployment model

This server is usually deployed as a local Windows companion process next to the target WPF application.

## Canonical script sources

Installer and packaging behavior are defined in `scripts/`, not in the documentation site:

- [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)
- [scripts/tools/packaging/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/tools/packaging/Publish-Release.ps1)
- [scripts/installer/Installer.Actions.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/installer/Installer.Actions.ps1)

## Recommended install modes

### Public release package

1. Download the architecture-matched `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).
2. Extract the package.
3. Run `run.bat`.

### Script-driven install

Review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) first. Then choose either a published-release bootstrap or local execution.

Remote example:

```powershell
$architecture = 'x64'
$version = (Invoke-RestMethod 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest').tag_name.TrimStart('v')
$assetName = "release_${version}_win-$architecture.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wpf-devtools-install-" + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $tempRoot $assetName
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
Invoke-WebRequest "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force
powershell -ExecutionPolicy Bypass -File (Join-Path $tempRoot 'bin\install.ps1')
```

Client-specific remote example:

```powershell
$architecture = 'x64'
$version = (Invoke-RestMethod 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest').tag_name.TrimStart('v')
$assetName = "release_${version}_win-$architecture.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wpf-devtools-install-" + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $tempRoot $assetName
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
Invoke-WebRequest "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force
powershell -ExecutionPolicy Bypass -File (Join-Path $tempRoot 'bin\install.ps1') -Version latest -Architecture $architecture -Client claude-code -NonInteractive -Force -OutputJson
```

Local example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -NonInteractive -Force -OutputJson
```

## Remote script execution is optional

Any remote bootstrap flow is optional. Review the repository source first and treat the script in `scripts/` as the authoritative implementation.

## Release layout matters

The bootstrapper and inspector sidecars are discovered relative to the server, so the documented release layout must stay stable across installs and upgrades.

See [Release Layout](release-layout.md) for the exact folder contract.

## Installed executable contract

The MCP client should launch the resolved installed `wpf-devtools-<arch>.exe` under the chosen install root, for example:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

If you do not pass `-InstallRoot`, the installer first reuses the last live install root when possible. Only when no reusable install root exists does it fall back to `%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe`.

## Production checklist

- Use the architecture that matches the target process.
- Keep the published `bin/inspectors` and `bin/bootstrapper` folders next to the installed server layout.
- Sign Release inspector binaries.
- Configure authentication and TLS settings for hardened environments.
- Validate `get_processes`, `connect`, and a scene-level call such as `get_ui_summary` from the installed path outside the repository.
