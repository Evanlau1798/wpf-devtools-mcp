# Claude Desktop 快速開始

Claude Desktop 使用靜態 JSON 設定檔，因此最乾淨的做法是直接複製 installer 產生的 JSON。

## 1. 安裝 WPF DevTools

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

安裝後的預設 executable 路徑是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 2. 使用 installer 產生的 JSON

installer 會輸出 `client-registration\claude-desktop.json`，格式如下：

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "command": "%LOCALAPPDATA%\\WpfDevToolsMcp\\x64\\current\\WpfDevTools.Mcp.Server.exe",
      "args": []
    }
  }
}
```

如果你需要切換架構，只要把對應的已安裝路徑更新到本機 `claude_desktop_config.json` 即可。

## 3. 第一個提示詞

```text
Use the WPF DevTools MCP server to connect to the running WPF app, auto-discover the target if there is only one visible candidate, and summarize the visual tree root.
```

## 注意事項

- 一般情況先從 `connect()` 開始；只有 auto-discovery 出現多個候選時，才使用 `get_processes(windowFilter)`。
- 變更狀態的工具留到確認 session 穩定後再使用。
- 如果切換 `x64`、`x86` 或 `arm64`，請重新安裝或重新註冊。
