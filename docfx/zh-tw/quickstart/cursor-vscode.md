# Cursor 與 VS Code 快速開始

Cursor 與 VS Code 最適合直接複製 installer 產生的 JSON 設定。

## 安裝後的可執行檔路徑

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## installer 產生的 JSON

`client-registration\cursor-vscode.json` 會包含：

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
