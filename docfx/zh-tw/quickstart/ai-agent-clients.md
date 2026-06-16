# AI Agent Client 快速開始

請先依照 [5 分鐘快速開始](index.md) 安裝 WPF DevTools，再把 installed executable 註冊到偏好的 MCP client。本頁只比較 client registration path。

## Installed executable

請以 generated `client-registration` artifact 作為真源。Executable 通常是：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 支援 client

| Client | Registration style | Guide |
| --- | --- | --- |
| Claude Code | Installer 產生的 CLI command | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | Installer 產生的 CLI command | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Claude Desktop | Installer 產生的 JSON config | [Claude Desktop](claude-desktop.md) |
| Cursor | Installer 產生的 `mcpServers` JSON | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| VS Code | Installer 產生的 `servers` JSON | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| Visual Studio | Installer 產生的 `servers` JSON | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| Other | Artifact-only JSON | 使用 `<InstallRoot>\<arch>\client-registration\other.mcpServers.json` |

## 第一次 verification flow

第一次連線前，請把 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 設為已審查 target 的 exact local absolute executable path。

1. `connect`
2. `get_active_process`
3. `get_ui_summary(depthMode: "semantic")`
4. `navigation.recommended` 建議的 focused diagnostic tool

請優先使用 scene-level summary，再考慮 visual-tree dump 或 screenshot。
