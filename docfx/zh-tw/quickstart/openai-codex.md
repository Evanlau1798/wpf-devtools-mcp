# OpenAI Codex 與 Codex CLI 快速開始

如果你要從 Codex workflow 使用已安裝的 WPF DevTools server，請使用這份指南。

## 1. 安裝 Codex CLI

```powershell
npm install -g @openai/codex
```

## 2. 安裝 WPF DevTools

建議的公開安裝路徑：

1. 從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載對應架構的 `release_<version>_win-<arch>.zip`。
2. 解壓縮套件。
3. 執行 `run.bat`。

如果你偏好腳本驅動安裝，請先審查 [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) 再於本機執行。

範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client codex -Force
```

安裝後的預設 executable 路徑是：

```text
%APPDATA%\WpfDevToolsMcp\x64\current\bin\wpf-devtools-x64.exe
```

## 3. 註冊 MCP server

可直接使用 `client-registration\codex.txt` 中的命令，或依照同樣格式改成你的實際安裝後絕對路徑：

```powershell
codex mcp add wpf-devtools -- "C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe"
```

## 4. 驗證註冊結果

```powershell
codex mcp list
```

## 5. 第一個實用 prompt

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 注意事項

- 即使你的編輯器或 agent workflow 跨環境，MCP server 本體仍需在 Windows 執行。
- 一般情況先從 `connect()` 開始；只有 auto-discovery 出現多個候選，或你想先看明確 target metadata 時，才使用 `get_processes(windowFilter)`。
- 在 tree-heavy inspection 前，優先使用 scene-level 工具。
- 如果你已經知道下一步工具，且希望回應更精簡，可在該次呼叫傳入 `navigation=false`。
- 若 `connect` 失敗，請一起檢查 server、bootstrapper 與 target process 的 bitness。
- Codex 使用 STDIO transport，因此請保持 `stdout` 乾淨。
- 如果目標 app 是 elevated，請以系統管理員權限啟動 Codex 或其宿主終端機。非系統管理員權限的 Codex host 通常看得到 process，但無法真正控制 elevated target。
