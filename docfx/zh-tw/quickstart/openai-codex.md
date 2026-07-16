# OpenAI Codex 與 Codex CLI 設定

請先依照 [5 分鐘快速開始](index.md) 安裝 WPF DevTools，再使用 generated Codex registration command。

一般 Codex setup 請使用 5-Minute Setup 的 installer path，不要使用 portable ZIP extraction。Portable ZIP checks 只適用於已審查本機 archive 或 offline validation。

## Register

Installer 會寫入：

```text
<InstallRoot>\<arch>\client-registration\codex.txt
```

請在 Codex CLI 可用的 shell 中執行該檔案列出的 command。它應指向：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## Verify

啟動 client 前，設定 reviewed target path 與 scene summary read gate：

```powershell
$target = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

接著要求 Codex：

```text
Use the WPF DevTools MCP server. Connect to the allowlisted WPF target and return get_ui_summary(depthMode: "semantic").
```

## 需要更深工具時的 gates

第一次 Codex session 先保持範圍小；後續只啟用下一個已核准 tool 需要的 gate：

- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 用於 `element_screenshot`。
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` 用於 `get_viewmodel`、command metadata，以及 snapshot 或 `batch_mutate` 內的 ViewModel scopes。
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` 用於 `capture_state_snapshot`、`batch_mutate`、interaction、event drain 與 restore workflows。
- `WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES=true` 搭配 exact `WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS` match，用於已確認的 `apply_ui_blueprint` 與 `apply_ui_project_integration` writes。啟用前先閱讀 [UI Composer apply 與 project-integration workflow](../reference/tools/ui-composer.md)。

在 mutation 或有順序的 `batch_mutate` 前，先呼叫 `capture_state_snapshot`，再檢查 `get_state_diff`，並在 workflow 需要 rollback 時 restore。

## Troubleshooting

- 以 generated `codex.txt` command 作為真源。
- 切換 architecture 或 install root 後重新註冊。
- 若 target 以系統管理員/elevated 身分執行，請用 matching elevated shell 啟動 client，或在 policy 擋下 elevation 時 prefer SDK-hosted diagnostics。
- Wrapper 不應寫入 stdout，因為 server transport 是 STDIO。
