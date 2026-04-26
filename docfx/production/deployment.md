# Deployment Guide

## Deployment model

This server is usually deployed as a local Windows companion process next to the target WPF application.

## Canonical script sources

Installer and packaging behavior are defined in `scripts/`, not in the documentation site:

- [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)
- [scripts/tools/packaging/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/tools/packaging/Publish-Release.ps1)
- [scripts/installer/Installer.Actions.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/installer/Installer.Actions.ps1)

## Recommended install modes

### Reviewed script-driven install

Review [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) as the maintainer source first. The reviewed installer resolves the matching GitHub Release asset, validates archive integrity before extraction, and then runs the version-matched packaged `bin/install.ps1` from that release.

Recommended example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64
```

Client-specific automation example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -NonInteractive -Force -OutputJson
```

### Public release package fallback

1. Download the architecture-matched `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) together with `SHA256SUMS.txt` and `release-assets.json`.
2. Verify the archive with `SHA256SUMS.txt` and `release-assets.json` before extraction.
3. Extract the package.
4. Run `run.bat`.

Before trusting the extracted package, keep the verified release sidecars beside the archive: `SHA256SUMS.txt` for the checksum and `release-assets.json` for the canonical release metadata. If the verified archive and those sidecars are no longer adjacent to the extracted package, set `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` (or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`) before launching `run.bat` so the local install still enforces an explicit signer pin.

`run.bat` requests elevation when the current shell is not already elevated and then launches the packaged `bin/install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.

## Remote script execution is optional

Any package-local bootstrap flow is optional. Review the repository source first and treat the script in `scripts/` as the authoritative implementation.

## Release layout matters

The bootstrapper and inspector sidecars are discovered relative to the server, so the documented release layout must stay stable across installs and upgrades.

See [Release Layout](release-layout.md) for the exact folder contract.

## Installed executable contract

The MCP client should launch the resolved installed `wpf-devtools-<arch>.exe` under the chosen install root, for example:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

If you do not pass `-InstallRoot`, the installer first reuses the last live install root when possible. Only when no reusable install root exists does it fall back to `%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe`.

## Signed payload provenance checklist

- Keep the verified `SHA256SUMS.txt` and `release-assets.json` beside the downloaded archive until after installation evidence is captured.
- Verify the signed release payloads, including `bin/wpf-devtools-<arch>.exe`, `bin/inspectors`, and `bin/bootstrapper`, against the pinned release signer. Set `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` when the verified sidecars are no longer adjacent to the extracted package.
- Run a package-local smoke check from the extracted package with `run.bat`, then verify `get_processes`, `connect`, and `get_ui_summary` before registering that install with user tools.
- Repeat the smoke check from the final installed path by launching `<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe`.

## Production checklist

- Use the architecture that matches the target process.
- Keep the published `bin/inspectors` and `bin/bootstrapper` folders next to the installed server layout.
- Sign Release inspector binaries.
- Configure authentication and TLS settings for hardened environments.
- Validate `get_processes`, `connect`, and a scene-level call such as `get_ui_summary` from the installed path outside the repository.
