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

建議做法：先在 repository root 開啟 PowerShell，再執行：

```powershell
$RepoRoot = (Get-Location).Path
codex mcp add wpf-devtools -- dotnet run --project "$RepoRoot\src\WpfDevTools.Mcp.Server" --no-build
```

這樣可以避免把磁碟機代號寫死，不論 repository 在 `C:`、`D:`、`E:` 或其他磁碟都可直接套用。

如果您是在其他資料夾執行命令，請改用明確的絕對路徑：

```powershell
codex mcp add wpf-devtools -- dotnet run --project "<ABSOLUTE_PATH_TO_REPO>\src\WpfDevTools.Mcp.Server" --no-build
```

這會把 MCP server 寫入 Codex 共用設定檔。

## 驗證註冊結果

```powershell
codex mcp list
```

## 建議的第一個 prompt

```text
List WPF processes, connect to the test app, ping it, and summarize the root visual tree.
```

## 如果您使用 Codex IDE extension

Codex 會共用同一份 MCP 設定給不同 client surface。實務上最簡單的做法是：

1. 先安裝 Codex CLI
2. 先執行一次 `codex mcp add ...`
3. 重新啟動 Codex client 或 IDE extension
4. 確認 `wpf-devtools` 出現在 MCP server 清單中

這樣您不需要為 CLI 與 IDE extension 維護兩份不同設定。

## Windows 重要說明

OpenAI 會持續更新 Codex client 的支援狀態，因此請以最新官方 Codex 文件為準確認 Windows 支援細節。

對這個專案來說，建議做法是：

- 如果您的環境可穩定使用，優先在 Windows shell 直接執行 Codex
- 如果 CLI 在您的 Windows 環境不穩定，優先改用 Codex IDE extension，但 MCP server 仍維持在 Windows 上
- 不要把 WPF DevTools server 本體搬到 Linux 或 WSL 內執行，因為它需要操作原生 Windows WPF process

## 這個專案的額外注意事項

- `connect` 只有在 bootstrapper architecture 與 target process architecture 一致時才會成功。
- MCP over STDIO 很依賴乾淨的 `stdout`，請避免 wrapper 輸出額外文字。
- 本機未簽章開發建議使用 `Debug` build；正式環境請改用已簽章的 `Release` build。

## 相關文件

- [AI Agent Client 快速開始](ai-agent-clients.md)
- [Cursor 與 VS Code](cursor-vscode.md)
- [AI Agent 使用指南](../guides/ai-agent-guide.md)
