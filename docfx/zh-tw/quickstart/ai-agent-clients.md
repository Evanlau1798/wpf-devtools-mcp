# AI Agent Client 快速開始總覽

公開安裝路徑的核心原則只有一個：所有 client 都應該啟動安裝後的可執行檔，而不是 source tree 裡的開發命令。

預設安裝路徑範例：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

安裝程式也會在下列資料夾產生各 client 可直接複製的設定：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\client-registration\
```

## 推薦路徑

| Client | 適合情境 | 設定方式 | 指南 |
| --- | --- | --- | --- |
| Claude Code | 終端機優先的 agent workflow | 使用 installer 產生的命令 | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | OpenAI CLI 與 agent workflow | 使用 installer 產生的命令 | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Claude Desktop | 桌面聊天工作流 | 使用 installer 產生的 JSON | [Claude Desktop](claude-desktop.md) |
| Cursor / VS Code | 編輯器優先工作流 | 使用 installer 產生的 JSON | [Cursor 與 VS Code](cursor-vscode.md) |

## 第一次驗證流程

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

## WPF 專案注意事項

- MCP server 必須跑在 Windows。
- 因為 transport 是 STDIO，所以 `stdout` 必須保持乾淨。
- server 與 bootstrapper 的 bitness 必須和目標 process 一致。
- `client-registration` 目錄是公開文件與實際安裝之間的 copy-paste 真實來源。
