# Claude Code 設定

Claude Code 適合 terminal-first workflow。請先依照 [5 分鐘快速開始](index.md) 安裝 WPF DevTools，再使用 generated Claude Code registration command。

## Register

Installer 會寫入：

```text
<InstallRoot>\<arch>\client-registration\claude-code.txt
```

請在 Claude Code 可用的 shell 中執行該檔案列出的 command。它應註冊 installed package executable，而不是 source-tree command。

Fallback executable shape：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## Verify

啟動 Claude Code 前，設定 reviewed target allowlist：

```powershell
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = 'C:\Path\To\YourApp.exe'
```

接著要求 Claude Code：

```text
Use the WPF DevTools MCP server. Connect to the allowlisted WPF target, then summarize the current UI with get_ui_summary(depthMode: "semantic").
```

## Troubleshooting

- 切換 install root 或 architecture 後，重新執行 generated registration command。
- Claude Code 可以 expose slash-command style MCP entry points，例如 `/mcp__wpf-devtools__...`，以及 resource mention `@wpf-devtools:`；registration source 仍以 generated `claude-code.txt` command 為準。
- 若 Claude Code 無法啟動 server，確認 command path 指向 installed package 下的 `wpf-devtools-<arch>.exe`。
- 若 `connect` 被 denied，先修正 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`。
