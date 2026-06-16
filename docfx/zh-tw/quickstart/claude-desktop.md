# Claude Desktop 設定

請先依照 [5 分鐘快速開始](index.md) 安裝 WPF DevTools，再把 generated Claude Desktop JSON 複製到本機 Claude Desktop configuration。

## Register

Installer 會寫入：

```text
<InstallRoot>\<arch>\client-registration\claude-desktop.json
```

結構如下：

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

請複製 generated artifact，而不是上方 sample path。

## Verify

啟動 Claude Desktop 前設定 reviewed target allowlist：

```powershell
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = 'C:\Path\To\YourApp.exe'
```

第一次 prompt：

```text
Use the WPF DevTools MCP server. Connect to the allowlisted WPF target, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Troubleshooting

- 修改 MCP configuration 後重啟 Claude Desktop。
- 變更 install root 或 architecture 後重新複製 generated JSON。
- 除非 workflow 明確需要，否則保持 mutation tools disabled。
