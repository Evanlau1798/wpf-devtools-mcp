# Release Layout

This page documents the stable public folder contract for published release assets, extracted packages, and installed copies.

## Canonical generation sources

- Packaging source: [scripts/release/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Publish-Release.ps1)
- Install source: [scripts/release/Install-WpfDevTools.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Install-WpfDevTools.ps1)
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
  install.bat
  install.ps1
  setup.ps1
  uninstall.ps1
  manifest.json
  bin/
    WpfDevTools.Mcp.Server.exe
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
```

## Installed layout

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\
  current/
    install.bat
    install.ps1
    setup.ps1
    uninstall.ps1
    manifest.json
    bin/
      WpfDevTools.Mcp.Server.exe
      WpfDevTools.Mcp.Server.dll
      WpfDevTools.Injector.dll
      WpfDevTools.Shared.dll
      inspectors/
      bootstrapper/
  client-registration/
    claude-code.txt
    codex-cli.txt
    claude-code.project.mcp.json
    claude-desktop.json
    cursor-vscode.json
    github-copilot-vscode.json
    other.mcpServers.json
  install-manifest.json
```

## Contract notes

- MCP clients should register `bin/WpfDevTools.Mcp.Server.exe`.
- `bin/inspectors` and `bin/bootstrapper` are sidecar folders and must remain adjacent to the installed server content.
- `install.bat` is the package-root entrypoint for users who do not want to invoke PowerShell directly.
- `setup.ps1` is the primary package installer for end users.
- `install.ps1` remains available for lower-level copy/install automation.
- `client-registration` is generated at install time and is the public copy-paste source for AI client setup.
