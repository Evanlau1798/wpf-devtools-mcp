# AI Agent Client 快速開始

先安裝 WPF DevTools，再將已安裝的執行檔註冊到你偏好的 client。

## 一鍵安裝

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

若要指定架構並一次產生多個 client 的設定，可使用：

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code,codex-cli -NonInteractive -Force
```

所有公開文件中的安裝流程，最終都應該指向已安裝的 executable，而不是 source tree 中的啟動命令。

預設安裝路徑範例：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

installer 也會在以下位置產生各 client 專用的註冊片段：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\client-registration\
```

## 建議路徑

| Client | 最適合的情境 | 註冊方式 | 指南 |
| --- | --- | --- | --- |
| Claude Code | 終端機導向的 agent workflow | installer 產生的命令 | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | OpenAI CLI 與 agent workflow | installer 產生的命令 | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Claude Desktop | 桌面聊天工作流 | installer 產生的 JSON | [Claude Desktop](claude-desktop.md) |
| Cursor / VS Code | 編輯器導向工作流 | installer 產生的 JSON | [Cursor 與 VS Code](cursor-vscode.md) |

## 第一次驗證流程

不管你選哪一種 client，第一次連線都建議照這個順序驗證：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`

## WPF 專案特有提醒

- MCP server 必須在 Windows 上執行。
- 由於 transport 是 STDIO，請保持 `stdout` 乾淨。
- server 與 bootstrapper 的 bitness 必須和 target process 相符。
- `client-registration` 目錄是公開安裝流程下的 copy-paste 真實來源。

下一步：選擇對應的 client 指南。
