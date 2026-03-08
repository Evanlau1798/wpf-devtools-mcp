# Cursor 與 VS Code 快速開始

## 本頁使用的路徑規則

VS Code 與 Cursor 的 MCP 設定通常都是 JSON，因此請使用絕對專案路徑，而不是任何機器專屬範例路徑。

請把 `<ABSOLUTE_PATH_TO_REPO>` 替換成您本機 clone 的完整路徑；它可以位於任何磁碟機，例如 `D:\dev\wpf-devtools-mcp` 或 `E:\repos\wpf-devtools-mcp`。

## VS Code MCP 設定

建立或更新 `.vscode/mcp.json`：

```json
{
  "servers": {
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

如果您想先從 repository root 解析出絕對路徑，可執行：

```powershell
Resolve-Path .\src\WpfDevTools.Mcp.Server
```

再把輸出的完整路徑貼進上方 JSON。

## Cursor 設定

在 Cursor 的 MCP server 設定中使用相同的 `command` / `args` 組合即可。

## 建議的工作區做法

- 讓 WPF 應用程式與 MCP server 分開在不同終端機中運行。
- 若您修改 analyzer 或 tool 程式碼，請重新建置 server。
- 不要讓額外日誌輸出污染 `stdout`，因為 MCP transport 依賴乾淨的 STDIO 通道。

## 第一批有用的操作

- 請編輯器內 agent 先呼叫 `tools/list`。
- 用 `get_processes` 找到目標 PID。
- 在進入任何特定行程工作流前，先執行 `connect`。
