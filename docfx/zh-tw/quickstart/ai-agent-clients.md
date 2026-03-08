# AI Agent Client 快速開始

這一頁幫助您用最短路徑選擇適合 WPF DevTools 的 MCP client。

在設定任何 client 之前，請先完成 [5 分鐘快速開始](index.md) 中的伺服器端步驟：建置 managed server、建置對應架構的 native bootstrapper，並啟動一個 WPF 目標程式。

## 推薦路徑

| Client | 最適合的情境 | 安裝方式 | MCP 註冊方式 | 指南 |
| --- | --- | --- | --- | --- |
| Claude Code | 終端機導向、希望一行安裝與一行註冊 | 一行命令 | 一行命令 | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | 使用 OpenAI agent 工作流、CLI 或 IDE extension | 一行命令 | 一行命令 | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Claude Desktop | 偏好桌面聊天介面 | 安裝 App | JSON 設定檔 | [Claude Desktop](claude-desktop.md) |
| Cursor / VS Code | 偏好編輯器內工作流 | 安裝 App 或 extension | MCP 設定 / JSON 檔 | [Cursor 與 VS Code](cursor-vscode.md) |

## 這些 client 最後都會執行的伺服器命令

所有快速開始流程最終都會指向同一個 STDIO MCP server 命令：

```powershell
dotnet run --project C:\src\wpf-devtools-mcp\src\WpfDevTools.Mcp.Server --no-build
```

請把 `C:\src\wpf-devtools-mcp` 替換成您實際 clone 的路徑。

## 第一次驗證流程

不論您選哪個 client，第一次連線都建議依照這個順序驗證：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

第一次健康驗證應該看到：

- `get_processes` 有列出目標 WPF process
- `connect` 成功，且沒有 architecture mismatch
- `ping` 很快回應
- `get_visual_tree` 回傳 root window 與子元素

## 給 AI client 的 WPF 專案重點

- 這個 server 只能在 Windows 上使用，目標也是執行中的 WPF process。
- MCP transport 是 STDIO，所以外層 wrapper 不可以污染 `stdout`。
- `connect` 只有在 bootstrapper architecture 與 target process architecture 一致時才會成功。
- 如果您使用 `--no-build`，請務必先完成建置再註冊到 AI client。

下一步：閱讀 [Claude Code](claude-code.md) 或 [OpenAI Codex 與 Codex CLI](openai-codex.md)
