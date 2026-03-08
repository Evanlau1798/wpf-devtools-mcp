# Claude Desktop 快速開始

Claude Desktop 適合直接套用 installer 產生的 JSON 設定。

## 安裝後的可執行檔路徑

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## installer 產生的 JSON

`client-registration\claude-desktop.json` 會包含：

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
