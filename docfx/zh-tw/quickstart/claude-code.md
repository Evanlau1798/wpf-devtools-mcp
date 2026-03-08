# Claude Code 快速開始

如果你偏好終端機導向的 agent workflow，Claude Code 會是目前最直接的公開使用方式。

## 1. 安裝 Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
```

## 2. 安裝 WPF DevTools

最快方式：

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

若要直接產生 Claude Code 註冊內容，可使用：

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code -NonInteractive -Force
```

安裝後的預設 executable 路徑是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 3. 註冊 MCP server

可直接使用 `client-registration\claude-code.txt` 中的命令，或手動執行：

```powershell
claude mcp add --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

若要做 project scope 註冊，可使用：

```powershell
claude mcp add --scope project --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

## 4. 驗證註冊結果

```powershell
claude mcp list
```

## 5. 第一個實用提示詞

```text
List WPF processes, connect to the target app, ping it, and show the first two levels of the visual tree.
```

## 注意事項

- server 必須執行在 Windows。
- 不要在 `WpfDevTools.Mcp.Server.exe` 外層再包會污染 `stdout` 的啟動器。
- 若 `connect` 失敗，先一起檢查 server、bootstrapper 與 target 的 bitness。
