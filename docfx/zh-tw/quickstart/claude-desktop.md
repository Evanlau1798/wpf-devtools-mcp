# Claude Desktop 快速開始

Claude Desktop 使用靜態 JSON 設定檔，因此最乾淨的做法是直接複製 installer 產生的 JSON。

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

## 3. 第一個 prompt

```text
Use the WPF DevTools MCP server to connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 注意事項

- 一般情況先從 `connect()` 開始；只有 auto-discovery 回報多個候選時，才使用 `get_processes(windowFilter)`。
- 在展開 visual tree 前，優先做 scene-level 驗證。
- mutation 工具請放到較後面的工作流再使用。
- 如果切換 `x64`、`x86` 或 `arm64`，請重新安裝或重新註冊。
