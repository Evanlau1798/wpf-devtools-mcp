# 部署指南

## 部署模型

這個 server 通常會以本機 Windows companion process 的形式部署，並與目標 WPF 應用程式並存。

## 正式腳本來源

installer 與 packaging 行為定義在 `scripts/`，而不是文件站台本身：

- [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)
- [scripts/release/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Publish-Release.ps1)
- [scripts/release/Install-WpfDevTools.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/release/Install-WpfDevTools.ps1)

## 建議安裝模式

### 公開 release package

1. 從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載符合架構的 `release_<version>_win-<arch>.zip`。
2. 解壓縮套件。
3. 執行 `setup.ps1 -Force`。

### 腳本驅動安裝

請先審查 [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)，再選擇遠端或本機執行。

遠端範例：

```powershell
irm https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1 | iex
```

範例：

```powershell
& ([scriptblock]::Create((irm 'https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1'))) -Version latest -Architecture x64 -Client claude-code -Force
```

本機範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -Force
```

## 遠端腳本執行是可選的

任何遠端 `irm | iex` 流程都只是可選方案。請先審查 repo 內的原始碼，並把 `scripts/` 視為唯一的權威實作。

## Release layout 很重要

bootstrapper 與 inspector sidecar 會相對於 server 位置被解析，因此文件中的 release layout 必須在安裝與升級後維持穩定。

完整契約請參考 [Release Layout](release-layout.md)。

## 已安裝 executable 契約

MCP client 應直接啟動安裝後的 `WpfDevTools.Mcp.Server.exe`，例如：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 生產環境檢查清單

- 使用與 target process 相同的架構。
- 保持 `inspectors` 與 `bootstrapper` 目錄與安裝後的 server 內容相鄰。
- 對 release inspector binaries 進行簽章。
- 在硬化環境中設定 authentication 與 TLS。
- 在 repository 外，從已安裝路徑實際驗證 `get_processes`、`connect`，以及一個 scene-level 呼叫，例如 `get_ui_summary`。
