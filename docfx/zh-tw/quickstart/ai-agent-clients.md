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
| Grok Build CLI | Installer 產生的 CLI command | 使用 `<InstallRoot>\<arch>\client-registration\grok.txt` |
| Claude Desktop | Installer 產生的 JSON config | [Claude Desktop](claude-desktop.md) |
| Cursor | Installer 產生的 `mcpServers` JSON | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| VS Code | Installer 產生的 `servers` JSON | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| Visual Studio | Installer 產生的 `servers` JSON | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| Other | Artifact-only JSON | 使用 `<InstallRoot>\<arch>\client-registration\other.mcpServers.json` |

使用 `-Client other -OutputJson` 時，install result 也會包含 `composerPolicyProfile`。其中的 `requiredEnvironment` map 會列出 `apply_ui_blueprint` 所需的最小 project-write 與 destructive gates；`freshServerProcessRequired=true` 則提醒自動化流程在設定完成後啟動新的 scoped server。

## 第一次 verification flow

第一次連線前，請把 reviewed target path 同時用於 connection 與 raw-injection fallback，並為第一次 scene summary 啟用 sensitive reads：

```powershell
$target = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

1. `connect`
2. `get_active_process`
3. `get_ui_summary(depthMode: "semantic")`
4. `navigation.recommended` 建議的 focused diagnostic tool

請優先使用 scene-level summary，再考慮 visual-tree dump 或 screenshot。

## 需要更深工具時的 gates

第一次 session 先保持範圍小；後續只啟用下一個已核准 tool 需要的 gate：

- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 用於 `element_screenshot`。
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` 用於 `get_viewmodel`、command metadata，以及 snapshot 或 `batch_mutate` 內的 ViewModel scopes。
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` 用於 `capture_state_snapshot`、`batch_mutate`、interaction、event drain 與 restore workflows。

在 mutation 或有順序的 `batch_mutate` 前，先呼叫 `capture_state_snapshot`，再檢查 `get_state_diff`，並在 workflow 需要 rollback 時 restore。
