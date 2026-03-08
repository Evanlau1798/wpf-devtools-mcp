# Claude Desktop 設定

## 設定檔位置

將 server 加入 `claude_desktop_config.json`：

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "G:\\wpf-devtools-mcp\\src\\WpfDevTools.Mcp.Server",
        "--no-build"
      ]
    }
  }
}
```

請把路徑改成你實際 clone 下來的資料夾位置。

## 建議的第一句提示詞

```text
使用 WPF DevTools MCP server 列出 processes，連線到測試 WPF app，並摘要 visual tree 的根節點。
```

## Claude 專用建議

- 任何 per-process 工具前，都先從 `get_processes` 與 `connect` 開始。
- 讓 Claude 動態探索工具 schema，而不是手寫固定 JSON payload。
- 使用互動工具時，優先採用「先檢查，再修改」的迭代式提示。

## 安全的前期操作順序

1. 先檢查 tree。
2. 再檢查 bindings 或 dependency properties。
3. 最後才呼叫 `set_dp_value`、`modify_viewmodel`、`override_style_setter` 這類會修改狀態的工具。
