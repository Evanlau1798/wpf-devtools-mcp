# Release Layout

本頁說明公開 release asset、解壓後 package，以及安裝後目錄的穩定契約。

## 公開 bootstrap asset

GitHub Pages 會提供 bootstrap installer：

```text
https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1
```

這個 bootstrap script 會下載下列其中一個 release asset：

- `WpfDevTools-win-x64.zip`
- `WpfDevTools-win-x86.zip`
- `WpfDevTools-win-arm64.zip`

## 解壓後 package 結構

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

## 安裝後結構

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

## 契約說明

- MCP client 應註冊 `WpfDevTools.Mcp.Server.exe`。
- `inspectors` 與 `bootstrapper` 是 sidecar 目錄，必須與安裝後的 server 內容相鄰。
- `setup.ps1` 是一般終端使用者的主要 package installer。
- `install.ps1` 保留給較低階的 copy/install 自動化。
- `client-registration` 會在安裝時產生，並作為 AI client setup 的 copy-paste 來源。
