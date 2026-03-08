# 部署指南

## 部署模型

這個 server 通常會以本機 Windows companion process 的形式部署，並與目標 WPF 應用程式並存。

## 建議安裝模式

### GitHub Pages bootstrap installer

公開的最快路徑是使用 GitHub Pages 提供的靜態 bootstrap script：

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

這個腳本會從 GitHub Releases 下載符合架構的 `WpfDevTools-win-<arch>.zip`，再執行 package 內的 `setup.ps1` installer。

### 離線或先審查再安裝

如果你不想使用 `irm | iex`，可以手動下載 release zip、先審查內容、解壓後再本機執行 `setup.ps1 -Force`。

## `irm | iex` 是選項，不是信任邊界

`irm | iex` 路徑可以提供最快安裝，但它只是 optional 選項，不應該是唯一信任模型。它也不是 SmartScreen bypass；SmartScreen reputation 與 code signing 仍然是不同層級的議題。

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
- 在 repository 外，從已安裝路徑實際驗證 `get_processes`、`connect` 與 `ping`。
