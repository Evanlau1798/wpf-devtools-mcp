# Cursor 與 VS Code 設定

## VS Code MCP 設定

在你的 MCP 設定檔中加入如下項目：

```json
{
  "servers": {
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

不同客戶端的鍵名可能略有差異，但核心原則不變：以 STDIO 啟動 `WpfDevTools.Mcp.Server`，不要額外包裝會污染 `stdout` 的 logging。

## 建議的第一個驗證提示詞

```text
列出執行中的 WPF processes，連線到測試 app，執行 ping，並顯示 visual tree 的第一層。
```

## 編輯器環境的實務建議

- 先確認 client 已正確載入工具清單，再開始要求 AI 執行實際互動。
- 若你同時開啟多個工作區，請確認只註冊一份指向目前 clone 的 server 設定。
- 若 `connect` 失敗，優先檢查架構與 bootstrapper 建置，而不是先懷疑 prompt 本身。

## 建議的安全工作流

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`
5. 需要時再進入 binding、DP、互動或 MVVM 工具
