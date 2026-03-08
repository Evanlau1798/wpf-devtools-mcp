# 部署指南

## 部署模型

這個 server 通常會以本機 Windows companion process 的形式部署在 WPF 目標程式旁邊。

## 建議安裝模式

### 較安全的預設模式

先下載 release package 或 installer script，檢查後再於本機執行。

### 便利模式

`irm | iex` 路徑可以提供最快安裝，但它只是 optional 選項，不應該是唯一信任模型。

## release layout 很重要

bootstrapper 與 inspector sidecar 會相對於 server 位置被解析，因此文件中的 release layout 必須在安裝與升級後維持穩定。

請參考 [Release Layout](release-layout.md)。

## 安裝後的可執行檔契約

MCP client 應直接啟動安裝後的 `WpfDevTools.Mcp.Server.exe`，例如：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```
