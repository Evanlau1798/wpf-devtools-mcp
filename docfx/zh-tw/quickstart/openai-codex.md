# OpenAI Codex 與 Codex CLI 快速開始

如果您要在 Codex workflow 中使用已安裝的 WPF DevTools server，請走這個流程。

## 1. 安裝 Codex CLI

```powershell
npm install -g @openai/codex
```

## 2. 安裝 WPF DevTools

安裝完成後，預設可執行檔路徑是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 3. 註冊 MCP server

可直接使用 `client-registration\codex-cli.txt`，或手動執行：

```powershell
codex mcp add wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

## 4. 驗證註冊

```powershell
codex mcp list
```
