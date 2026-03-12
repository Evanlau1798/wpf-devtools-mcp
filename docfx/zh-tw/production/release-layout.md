# Release Layout

此頁說明公開 release asset、解壓後 package，以及安裝後目錄的穩定結構契約。

## 公開 bootstrap asset

GitHub Pages 會提供 bootstrap installer：

```text
https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1
```

bootstrap script 會下載其中一個 GitHub Release asset：

- `release_{version}_win-x64.zip`
- `release_{version}_win-x86.zip`
- `release_{version}_win-arm64.zip`

## 解壓後 package 結構

```text
release_{version}_win-x64/
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

## 安裝後目錄結構

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\
  current/
    install.bat
    install.ps1
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
    claude-desktop.json
    cursor-vscode.json
    github-copilot-vscode.json
    other.mcpServers.json
  install-manifest.json
```

## 契約說明

- MCP client 應註冊 `bin/WpfDevTools.Mcp.Server.exe`
- `bin/inspectors` 與 `bin/bootstrapper` 是 sidecar 目錄，必須與安裝後的 server 內容保持相對位置
- `install.bat` 是 package root 的使用者入口，適合不想直接執行 PowerShell 的使用者
- `setup.ps1` 是主要的互動式 package installer
- `install.ps1` 保留給較低階的 copy/install 自動化流程
- `client-registration` 會在安裝時產生，並作為 AI client setup 的公開 copy-paste 來源
