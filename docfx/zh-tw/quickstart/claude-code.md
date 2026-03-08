# Claude Code 快速開始

如果您偏好終端機式 AI agent workflow，Claude Code 是最快的公開安裝路徑。

## 1. 安裝 Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
```

## 2. 安裝 WPF DevTools

完成 WPF DevTools 安裝後，預設可執行檔路徑是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 3. 註冊 MCP server

可直接使用 `client-registration\claude-code.txt` 內的命令，或手動執行：

```powershell
claude mcp add --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

Project scope 版本：

```powershell
claude mcp add --scope project --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

## 4. 驗證註冊

```powershell
claude mcp list
```
