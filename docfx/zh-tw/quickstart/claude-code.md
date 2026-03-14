# Claude Code 快速開始

如果你偏好終端機導向的 agent workflow，Claude Code 會是目前最直接的公開使用方式。

## 1. 安裝 Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
```

## 2. 安裝 WPF DevTools

最快方式：

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

> 安全提醒：在敏感環境執行 `irm | iex` 前，請先審查這個 hosted installer script 的內容。

如果你不想使用 `irm | iex`，請手動下載 release zip、先檢查內容，再於本機執行 `setup.ps1 -Force`，之後再註冊 Claude Code。

若要直接產生 Claude Code 註冊內容，可使用：

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients claude-code -NonInteractive -Force
```

安裝後的預設 executable 路徑是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 3. 註冊 MCP server

可直接使用 `client-registration\claude-code.txt` 中的命令，或手動執行：

```powershell
claude mcp add --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

若要做 project scope 註冊，可使用：

```powershell
claude mcp add --scope project --transport stdio wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

installer 也會輸出 `client-registration\claude-code.project.mcp.json`。若你要讓團隊成員或 CI worktree 使用一致的 project scope 設定，建議優先使用這個檔案。

## 4. 驗證註冊結果

```powershell
claude mcp list
```

## 5. 第一個實用提示詞

```text
Use the WPF DevTools MCP server to connect to the running WPF app, auto-discover the target if there is only one visible candidate, and summarize the root UI state.
```

## 6. 在 Claude Code 內做 discovery

- prompts 會以 `/mcp__wpf-devtools__connect_and_list_windows` 這類 slash commands 形式出現。
- resources 會以 `@wpf-devtools:capabilities` 與 `@wpf-devtools:limitations/elevated-targets` 這類引用形式出現。
- 當 Claude Code 知道 server 已存在，但不容易挑到正確工具時，這些入口會比自由敘述更穩定。

## 注意事項

- server 必須執行在 Windows。
- 不要在 `WpfDevTools.Mcp.Server.exe` 外層再包會污染 `stdout` 的啟動器。
- 一般情況先從 `connect()` 開始；只有 auto-discovery 出現多個候選，或你想先看明確 target metadata 時，才使用 `get_processes(windowFilter)`。
- 若 `connect` 失敗，先一起檢查 server、bootstrapper 與 target 的 bitness。
- 如果目標 app 是 elevated，請以系統管理員權限啟動 Claude Code，讓它透過 STDIO 拉起的 MCP server 能在相同完整性等級下 attach。
