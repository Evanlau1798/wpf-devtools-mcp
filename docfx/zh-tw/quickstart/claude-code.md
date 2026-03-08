# Claude Code 快速開始

如果您偏好終端機導向的 AI agent 工作流，希望同時擁有一行安裝與一行 MCP 註冊，這是最快的路徑。

開始前，請先完成 [5 分鐘快速開始](index.md) 的前 5 個步驟，確保 server 與 bootstrapper 都已建置完成。

## Windows 一行安裝

PowerShell：

```powershell
irm https://claude.ai/install.ps1 | iex
```

WinGet 替代方案：

```powershell
winget install Anthropic.ClaudeCode
```

## 一行註冊 MCP server

建議做法：先在 repository root 開啟 PowerShell，再執行：

```powershell
$RepoRoot = (Get-Location).Path
claude mcp add --transport stdio wpf-devtools -- dotnet run --project "$RepoRoot\src\WpfDevTools.Mcp.Server" --no-build
```

這樣可以避免把磁碟機代號寫死，不論 repository 在 `C:`、`D:`、`E:` 或其他磁碟都可直接套用。

如果您是在其他資料夾執行命令，請改用明確的絕對路徑：

```powershell
claude mcp add --transport stdio wpf-devtools -- dotnet run --project "<ABSOLUTE_PATH_TO_REPO>\src\WpfDevTools.Mcp.Server" --no-build
```

這會把 server 以 `wpf-devtools` 的名稱註冊到 Claude Code，並透過 STDIO 啟動。

## 選用：project-scoped registration

如果您希望 MCP 設定跟著 repository 走，而不是放在全域機器設定，請在 repository root 執行：

```powershell
$RepoRoot = (Get-Location).Path
claude mcp add --scope project --transport stdio wpf-devtools -- dotnet run --project "$RepoRoot\src\WpfDevTools.Mcp.Server" --no-build
```

當您希望隊友也能直接看到這個 repository 預期的 MCP 設定時，這種方式特別有用。

## 驗證 Claude Code 是否看得到 server

```powershell
claude mcp list
```

之後在 repository 中啟動 Claude Code，並使用這個第一個 prompt：

```text
List WPF processes, connect to the test app, ping it, and show me the top two levels of the visual tree.
```

## 如果您使用內建測試 App

請保持這兩個終端機開著：

Terminal 1：

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

Terminal 2 則會由上面的 MCP 註冊命令交給 Claude Code 管理。

## 這個專案下的 Claude Code 注意事項

- 本機未簽章開發優先使用 `Debug` build。
- MCP server 必須留在 Windows 上執行，因為目標 WPF process 與 bootstrapper 都是 Windows 原生。
- 不要在 server 外層包會對 `stdout` 寫入日誌的 wrapper。
- 如果 `connect` 失敗，先檢查 architecture；server bitness 與 target process bitness 必須一致。

## 相關文件

- [AI Agent Client 總覽](ai-agent-clients.md)
- [Claude Desktop 快速開始](claude-desktop.md)
- [AI Agent 使用指南](../guides/ai-agent-guide.md)
