# OpenAI Codex 與 Codex CLI 快速開始

如果您想要用 OpenAI Codex 來驅動 WPF DevTools，最簡單的方式是先安裝 Codex CLI，用一條命令註冊 MCP server，之後再重用同一份設定。

開始前，請先完成 [5 分鐘快速開始](index.md) 的前 5 個步驟，確保 server 與 bootstrapper 都已建置完成。

## Codex CLI 一行安裝

```powershell
npm install -g @openai/codex
```

安裝完成後，請先啟動一次 Codex 並完成登入：

```powershell
codex
```

## 一行註冊 MCP server

請先把下面命令中的專案路徑改成您的本機路徑：

```powershell
codex mcp add wpf-devtools -- dotnet run --project C:\src\wpf-devtools-mcp\src\WpfDevTools.Mcp.Server --no-build
```

這會把 MCP server 寫入 Codex 共用的設定檔。

## 驗證註冊結果

```powershell
codex mcp list
```

## 建議的第一個 prompt

```text
List WPF processes, connect to the test app, ping it, and summarize the root visual tree.
```

## 如果您使用 Codex IDE extension

Codex 文件使用同一份 MCP 設定檔來讓不同 Codex client 共用 server 設定。最簡單的做法是：

1. 先安裝 Codex CLI
2. 先執行一次 `codex mcp add ...`
3. 重新啟動 Codex client 或 IDE extension
4. 確認 `wpf-devtools` 出現在 MCP server 清單中

這樣您不需要為 CLI 與 IDE extension 維護兩份不同設定。

## Windows 重要說明

依 OpenAI 目前文件，Codex CLI 在 Windows 上仍屬 experimental。

對這個專案來說，建議做法是：

- 如果您的環境可穩定使用，優先在 Windows shell 直接執行 Codex
- 如果 CLI 在您的 Windows 環境不穩定，優先改用 Codex IDE extension
- 不要把 WPF DevTools server 本體搬到 Linux 或 WSL 內執行，因為它需要操作原生 Windows WPF process

## 這個專案的額外注意事項

- `connect` 只有在 bootstrapper architecture 與 target process architecture 一致時才會成功。
- MCP over STDIO 很依賴乾淨的 `stdout`，請避免 wrapper 輸出額外文字。
- 本機未簽章開發建議使用 `Debug` build；正式環境請改用已簽章的 `Release` build。

## 相關文件

- [AI Agent Client 快速開始](ai-agent-clients.md)
- [Cursor 與 VS Code](cursor-vscode.md)
- [AI Agent 使用指南](../guides/ai-agent-guide.md)
