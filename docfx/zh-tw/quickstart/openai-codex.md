# OpenAI Codex 與 Codex CLI 快速開始

如果你要從 Codex workflow 使用已安裝的 WPF DevTools server，請使用這份指南。

## 1. 安裝 Codex CLI

```powershell
npm install -g @openai/codex
```

## 2. 安裝 WPF DevTools

最快方式：

```powershell
irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1 | iex
```

> 安全提醒：在敏感環境執行 `irm | iex` 前，請先審查這個 hosted installer script 的內容。

如果你不想使用 `irm | iex`，請手動下載 release zip、先檢查內容，再於本機執行 `setup.ps1 -Force`，之後再註冊 Codex。

若要直接產生 Codex CLI 註冊內容，可使用：

```powershell
& ([scriptblock]::Create((irm https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1))) -Architecture x64 -Clients codex-cli -NonInteractive -Force
```

安裝後的預設 executable 路徑是：

```text
%LOCALAPPDATA%\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe
```

## 3. 註冊 MCP server

可直接使用 `client-registration\codex-cli.txt` 中的命令，或手動執行：

```powershell
codex mcp add wpf-devtools -- "$env:LOCALAPPDATA\WpfDevToolsMcp\x64\current\WpfDevTools.Mcp.Server.exe"
```

## 4. 驗證註冊結果

```powershell
codex mcp list
```

## 5. 第一個實用提示詞

```text
Use the WPF DevTools MCP server to connect to the running WPF app, auto-discover the target if there is only one visible candidate, and summarize the root visual tree.
```

## 注意事項

- 即使你的編輯器或 agent workflow 跨環境，MCP server 本體仍需在 Windows 執行。
- 一般情況先從 `connect()` 開始；只有 auto-discovery 出現多個候選，或你想先看明確 target metadata 時，才使用 `get_processes(windowFilter)`。
- 若 `connect` 失敗，請一起檢查 server、bootstrapper 與 target process 的 bitness。
- Codex 使用 STDIO transport，因此請保持 `stdout` 乾淨。
- 如果目標 app 是 elevated，請以系統管理員權限啟動 Codex 或其宿主終端機。非系統管理員權限的 Codex host 通常看得到 process，但無法真正控制 elevated target。
