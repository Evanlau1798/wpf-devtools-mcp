# Release Layout

This page documents the stable public folder contract for published release assets, extracted packages, and installed copies.

## Canonical generation sources

- Packaging source: `scripts/tools/packaging/Publish-Release.ps1`
- Sidecar source: `scripts/tools/packaging/Write-ReleaseSidecars.ps1`
- Upload source: `scripts/tools/packaging/Export-GitHubReleaseAssets.ps1`
- Online installer source: `scripts/online-installer.ps1`

The documentation below describes the output of those scripts. It does not replace them.

## Public release assets

Preview pre-release command until the first stable GitHub Release is published:

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

Current public onboarding uses `-Prerelease`; after the first stable GitHub Release is published, stable installs can omit that switch.

The generic archive pattern is `release_<version>_win-<arch>.zip`. Current per-architecture release archives are named:

- `release_<version>_win-x64.zip`
- `release_<version>_win-x86.zip`
- `release_<version>_win-arm64.zip`

Keep the archive adjacent to these sidecars for production review:

| File | Meaning |
| --- | --- |
| `SHA256SUMS.txt` | Archive checksum verification |
| `release-assets.json` | Canonical asset metadata and sidecar hashes |
| `release-sbom.spdx.json` | Release asset/archive inventory |
| `package-sbom.spdx.json` | Package, dependency, script, assembly, and payload SBOM |
| `release-evidence.json` | Release evidence and audit bundle |

`release-sbom.spdx.json` and `package-sbom.spdx.json` are intentionally separate. Neither replaces signer trust; Release payload signature verification still requires `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`.

## Extracted package layout

```text
release_<version>_win-x64/
  run.bat
  bin/
    install.ps1
    manifest.json
    wpf-devtools-x64.exe
    WpfDevTools.Mcp.Server.dll
    WpfDevTools.Injector.dll
    WpfDevTools.Shared.dll
    inspectors/
      net8.0-windows/
        WpfDevTools.Inspector.dll
      net48/
        WpfDevTools.Inspector.dll
    bootstrapper/
      x64/
        WpfDevTools.Bootstrapper.x64.dll
    installer/
      installer-helpers.manifest.json
      Installer.Actions.ps1
      Installer.Uninstall.ps1
      Tui.Flow.ps1
      ...
```

## Installed layout

```text
<InstallRoot>\<arch>\
  current/
    bin/
      manifest.json
      wpf-devtools-<arch>.exe
      WpfDevTools.Mcp.Server.dll
      WpfDevTools.Injector.dll
      WpfDevTools.Shared.dll
      inspectors/
      bootstrapper/
      installer/
  client-registration/
    claude-code.txt
    codex.txt
    claude-desktop.json
    cursor.global.json
    cursor.project.json
    vscode.json
    visual-studio.json
    other.mcpServers.json
  install-manifest.json
```

## Contract notes

- MCP clients should register `bin/wpf-devtools-<arch>.exe`.
- `bin/inspectors` and `bin/bootstrapper` must remain adjacent to the installed server content.
- `bin/installer` is the integrity-checked helper bundle used by package and recovery flows.
- `run.bat` is the package-root entrypoint for users who do not want to invoke PowerShell directly.
- `client-registration` is generated at install time and is the public copy-paste source for MCP client setup.
- If `-InstallRoot` is omitted, the installer reuses the last live install root when possible; `%APPDATA%\WpfDevToolsMcp` is the fallback root only when no reusable install root exists.

## Online installer source contract

`scripts/online-installer.ps1` is the canonical source entrypoint for the public installer alias and the generated single-file release artifact. The release pipeline must validate the source entrypoint, packaged artifact, and public alias together so they resolve the same release assets and enforce the same package verification behavior.

Any future helper reorganization must preserve that contract and keep the packaged helper manifest, generated artifact, and public installer endpoint in sync.
