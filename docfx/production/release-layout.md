# Release Layout

This page documents the stable public folder contract for published release assets, extracted packages, and installed copies.

## Public bootstrap asset

GitHub Pages hosts the bootstrap installer at:

```text
https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1
```

That bootstrap script downloads one of these release assets:

- `WpfDevTools-win-x64.zip`
- `WpfDevTools-win-x86.zip`
- `WpfDevTools-win-arm64.zip`

## Extracted package layout

```text
WpfDevTools-win-x64/
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
  setup.ps1
  install.ps1
  uninstall.ps1
  manifest.json
```

## Installed layout

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\
  current/
    WpfDevTools.Mcp.Server.exe
    WpfDevTools.Mcp.Server.dll
    WpfDevTools.Injector.dll
    WpfDevTools.Shared.dll
    inspectors/
    bootstrapper/
  client-registration/
    claude-code.txt
    codex-cli.txt
    claude-desktop.json
    cursor-vscode.json
  install-manifest.json
```

## Contract notes

- MCP clients should register `WpfDevTools.Mcp.Server.exe`.
- `inspectors` and `bootstrapper` are sidecar folders and must remain adjacent to the installed server content.
- `setup.ps1` is the primary package installer for end users.
- `install.ps1` remains available for lower-level copy/install automation.
- `client-registration` is generated at install time and is the public copy-paste source for AI client setup.
