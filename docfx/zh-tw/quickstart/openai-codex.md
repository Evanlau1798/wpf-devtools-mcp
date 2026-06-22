# OpenAI Codex 與 Codex CLI 設定

請先依照 [5 分鐘快速開始](index.md) 安裝 WPF DevTools，再使用 generated Codex registration command。

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

## Troubleshooting

- 以 generated `codex.txt` command 作為真源。
- 切換 architecture 或 install root 後重新註冊。
- 若 target 以系統管理員/elevated 身分執行，請用 matching elevated shell 啟動 client，或在 policy 擋下 elevation 時 prefer SDK-hosted diagnostics。
- Wrapper 不應寫入 stdout，因為 server transport 是 STDIO。
