# AI Agent Client 快速開始

先安裝 WPF DevTools，再把安裝後的執行檔註冊到你偏好的 client。

## 安裝真源

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)
- Online installer source: [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)

建議的公開安裝路徑：

```powershell
irm https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1 | iex
```

指定 client 的範例：

```powershell
& ([scriptblock]::Create((irm 'https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1'))) -Version latest -Architecture x64 -Client claude-code -Force
```

手動 package 的替代路徑：

1. 從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載 `release_<version>_win-<arch>.zip`。
2. 解壓縮套件。
3. 執行 `run.bat`。

本機腳本範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -Force
```

所有支援的 setup 路徑最後都應該啟動安裝後的執行檔，而不是 source tree 內的命令。

預設安裝路徑範例：

```text
%APPDATA%\WpfDevToolsMcp\x64\current\bin\wpf-devtools-x64.exe
```

線上安裝腳本與手動 package 安裝都會在下列位置產生 client-specific registration artifact：

```text
%APPDATA%\WpfDevToolsMcp\x64\client-registration\
```

## 建議選擇

| Client | 最適合的情境 | 註冊方式 | 指南 |
| --- | --- | --- | --- |
| Claude Code | 終端機導向的 agent workflow | installer 產生的 command | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | OpenAI CLI 與 agent workflow | installer 產生的 command | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Claude Desktop | 桌面聊天 workflow | installer 產生的 JSON config | [Claude Desktop](claude-desktop.md) |
| VS Code / Visual Studio | 編輯器導向 workflow | installer 產生的 JSON config | [VS Code 與 Visual Studio](cursor-vscode.md) |

## 第一次驗證流程

不論你選哪個 client，都建議依序驗證：

1. `connect`
2. 如果 auto-discovery 回報多個候選，先呼叫 `get_processes(windowFilter)`，再重試 `connect(processId)`
3. `get_ui_summary(depthMode: "semantic")`
4. 只有當 summary 還不夠時，才使用 `get_element_snapshot` 或 `get_visual_tree`
5. 只有需要明確存活檢查時才呼叫 `ping`

## WPF 特有提醒

- MCP server 必須跑在 Windows 上。
- 請保持 `stdout` 乾淨，因為 transport 是 STDIO。
- server 與 bootstrapper 位元數必須和 target process 一致。
- `client-registration` 產物是最可靠的 copy-paste 真源。
- 在需要之前，先用 scene-level 工具，不要太早展開整棵 tree 或索取完整 screenshot。

下一步：選擇你要使用的 client-specific 指南。
