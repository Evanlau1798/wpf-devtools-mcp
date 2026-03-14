# Cursor 與 VS Code 快速開始

Cursor 與 VS Code 最適合直接套用 installer 產生的 JSON 設定。

## 1. 安裝 WPF DevTools

建議的公開安裝路徑：

1. 從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載對應架構的 `release_<version>_win-<arch>.zip`。
2. 解壓縮套件。
3. 執行 `setup.ps1 -Force`。

如果你偏好腳本驅動安裝，請先審查 [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) 再於本機執行。

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
4. 執行 `get_ui_summary(depthMode: "semantic")`。
5. 只有在仍需要更深層結構時才使用 `get_element_snapshot` 或 `get_visual_tree`。

## 注意事項

- 若切換架構，請重新註冊已安裝的 executable。
- 避免讓編輯器外層 wrapper 把額外訊息寫入 `stdout`。
- 在 editor-driven workflow 中，先用 scene-level 摘要，再展開 tree。
