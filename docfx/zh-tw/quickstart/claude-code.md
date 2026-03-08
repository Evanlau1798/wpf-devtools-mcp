# Claude Code 快速開始

如果您想要用終端機導向的 AI agent 工作流，而且希望用一行命令安裝、一行命令註冊 MCP server，這是目前最直接的路徑。

開始前，請先完成 [5 分鐘快速開始](index.md) 的前 5 個步驟，確保 server 與 bootstrapper 都已建置完成。

## Windows 一行安裝

PowerShell：

```powershell
irm https://claude.ai/install.ps1 | iex
```

也可以使用 WinGet：

```powershell
winget install Anthropic.ClaudeCode
```

## 一行註冊 MCP server

請先把下面命令中的專案路徑改成您的本機路徑，再於 PowerShell 執行：

```powershell
claude mcp add --transport stdio wpf-devtools -- dotnet run --project C:\src\wpf-devtools-mcp\src\WpfDevTools.Mcp.Server --no-build
```

這會把 server 以 `wpf-devtools` 名稱註冊進 Claude Code，並指定使用 STDIO 啟動。

## 可選：改成 project scope

如果您希望 MCP 設定跟著 repository，而不是寫進全域設定，可以改用：

```powershell
claude mcp add --scope project --transport stdio wpf-devtools -- dotnet run --project C:\src\wpf-devtools-mcp\src\WpfDevTools.Mcp.Server --no-build
```

這種方式適合團隊共用同一份 repo 設定。

## 驗證 Claude Code 是否看得到 server

```powershell
claude mcp list
```

然後在 repository 內開啟 Claude Code，輸入這個第一個 prompt：

```text
List WPF processes, connect to the test app, ping it, and show me the top two levels of the visual tree.
```

## 如果您使用內建的測試 WPF App

請保持這兩個終端機狀態：

終端機 1：

```powershell
dotnet run --project tests/WpfDevTools.Tests.TestApp --no-build
```

終端機 2 會由 Claude Code 透過上面的 MCP 註冊命令自動管理。

## 這個專案在 Claude Code 上的注意事項

- 本機未簽章開發建議使用 `Debug` build。
- MCP server、target WPF process、bootstrapper 都應該留在 Windows 環境內執行。
- 不要額外包一層會寫入 `stdout` 的 wrapper script。
- 如果 `connect` 失敗，請先檢查 process architecture 是否一致。

## 相關文件

- [AI Agent Client 快速開始](ai-agent-clients.md)
- [Claude Desktop](claude-desktop.md)
- [AI Agent 使用指南](../guides/ai-agent-guide.md)
