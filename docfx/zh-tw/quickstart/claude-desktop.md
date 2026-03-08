# Claude Desktop 快速開始

## 本頁使用的路徑規則

Claude Desktop 使用靜態 JSON 設定檔，因此 server 設定應該填入絕對專案路徑。

請把 `<ABSOLUTE_PATH_TO_REPO>` 替換成您本機 clone 的完整路徑；它可以位於任何磁碟機，例如 `D:\dev\wpf-devtools-mcp` 或 `E:\repos\wpf-devtools-mcp`。

## 設定檔

把 server 加到 `claude_desktop_config.json`。

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<ABSOLUTE_PATH_TO_REPO>\\src\\WpfDevTools.Mcp.Server",
        "--no-build"
      ]
    }
  }
}
```

如果您目前就在 repository root，也可以先執行下面命令取得絕對路徑：

```powershell
Resolve-Path .\src\WpfDevTools.Mcp.Server
```

再把輸出的完整路徑貼進上方 JSON。

## 建議的第一個 prompt

```text
Use the WPF DevTools MCP server to list processes, connect to the test WPF app, and summarize the visual tree root.
```

## 給 Claude 的實用建議

- 先用 `get_processes` 與 `connect`，再要求任何特定行程工具。
- 請 Claude 動態探索 tool schema，不要一開始就硬編 JSON payload。
- 使用互動工具時，優先採取「先檢查、再修改」的漸進式提示。

## 前期安全工作流

1. 先檢查 tree。
2. 再檢查 bindings 或 dependency properties。
3. 最後才呼叫 `set_dp_value`、`modify_viewmodel`、`override_style_setter` 之類的變更型工具。
