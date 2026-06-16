# Cursor、VS Code 與 Visual Studio 設定

請先依照 [5 分鐘快速開始](index.md) 安裝 WPF DevTools，再複製對應 client 的 generated editor JSON。

## Generated artifacts

```text
<InstallRoot>\<arch>\client-registration\cursor.global.json
<InstallRoot>\<arch>\client-registration\cursor.project.json
<InstallRoot>\<arch>\client-registration\vscode.json
<InstallRoot>\<arch>\client-registration\visual-studio.json
```

Cursor 使用 `mcpServers`：

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

VS Code 與 Visual Studio 使用 `servers`：

```json
{
  "servers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

請以 generated artifacts 作為真源，不要使用 sample paths。

## Verify

啟動 editor client 前設定 reviewed target allowlist：

```powershell
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = 'C:\Path\To\YourApp.exe'
```

第一次 workflow：

1. `connect`
2. `get_active_process`
3. `get_ui_summary(depthMode: "semantic")`
4. `navigation.recommended` 建議的 focused diagnostic

## Notes

- Cursor global 與 project registration 可以共存，但同一 scope 只保留一個 active `wpf-devtools` entry。
- Cursor project registration 對應 `.cursor\mcp.json`；請以 `cursor.project.json` 作為 generated source。
- 切換 architecture 後重新註冊。
- Editor wrapper 不應寫入 stdout。
