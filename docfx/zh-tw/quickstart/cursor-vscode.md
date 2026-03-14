# Cursor 與 VS Code 快速開始

Cursor 與 VS Code 最適合直接套用 installer 產生的 JSON 設定。

## 1. 安裝 WPF DevTools

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

> 安全提醒：在敏感環境執行 `irm | iex` 前，請先審查這個 hosted installer script 的內容。

如果你不想使用 `irm | iex`，請手動下載 release zip、先檢查內容，再於本機執行 `setup.ps1 -Force`，之後再複製產生的 JSON。

安裝後的預設 executable 路徑是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 2. 使用 installer 產生的 JSON

installer 會輸出 `client-registration\cursor-vscode.json`，格式如下：

```json
{
  "servers": {
    "wpf-devtools": {
      "command": "%LOCALAPPDATA%\\WpfDevToolsMcp\\x64\\current\\WpfDevTools.Mcp.Server.exe",
      "args": []
    }
  }
}
```

將同一段 command path 套用到 Cursor 或 `.vscode/mcp.json` 即可。

## 3. 第一個實用流程

1. 請 client 先呼叫 `tools/list`。
2. 執行 `connect()`。
3. 若 auto-discovery 回傳多個候選，執行 `get_processes(windowFilter)` 並重新執行 `connect(processId)`。
4. 執行 `get_visual_tree`。

## 注意事項

- 若切換架構，請重新註冊已安裝的 executable。
- 避免讓編輯器外層 wrapper 把額外訊息寫入 `stdout`。
