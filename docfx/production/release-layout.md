# Release Layout

This page documents the stable public folder contract for published release assets, extracted packages, and installed copies.

## Canonical generation sources

- Packaging source: [scripts/tools/packaging/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/tools/packaging/Publish-Release.ps1)
- Online installer source: [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)

The documentation below describes the output of those scripts. It does not replace them.

## Public release assets

The current release archives are named:

- `release_<version>_win-x64.zip`
- `release_<version>_win-x86.zip`
- `release_<version>_win-arm64.zip`

Download them from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases).

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
        installer-helpers.manifest.json
        Installer.Actions.ps1
        Installer.Uninstall.ps1
        Tui.Flow.ps1
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
- `bin/inspectors` and `bin/bootstrapper` are sidecar folders and must remain adjacent to the installed server content.
- `bin/installer` is the integrity-checked helper bundle used by the packaged installer and standalone recovery flows; keep it adjacent to the packaged or installed server content.
- `run.bat` is the package-root entrypoint for users who do not want to invoke PowerShell directly.
- `bin/install.ps1` is the packaged copy of the canonical TUI-first installer script with CLI fallback.
- `client-registration` is generated at install time and is the public copy-paste source for AI client setup.
- If `-InstallRoot` is omitted, the installer reuses the last live install root when possible; `%APPDATA%\WpfDevToolsMcp` is only the fallback root when no reusable install root exists.
