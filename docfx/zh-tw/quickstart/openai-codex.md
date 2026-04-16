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
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client codex -NonInteractive -Force -OutputJson
```

如果 installer 不能重用先前仍有效的 live install root，且你也沒有傳入 `-InstallRoot`，回退用的 executable 路徑會是：

```text
%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
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

以下保留英文，是為了方便直接貼給 client：

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 注意事項

- 即使你的編輯器或 agent workflow 跨環境，MCP server 本體仍需在 Windows 執行。
- 一般情況先從 `connect()` 開始；只有 auto-discovery 出現多個候選，或你想先看明確 target metadata 時，才使用 `get_processes(windowFilter)`。
- 在 tree-heavy inspection 前，優先使用 scene-level 工具。
- 每次診斷、互動或 mutation 後，優先遵循 `navigation.recommended`，並把 `nextSteps` 視為相容欄位。
- 如果你已經知道下一步工具，且希望回應更精簡，具備額外 optional args 傳遞能力的 client 可在 `get_binding_errors` 呼叫傳入 `navigation=false`；schema-driven client 可以在這個工具上依賴這個 opt-out，因為它今天已經公告在 tool schema 中，但不應假設其他工具今天也有公開這個參數。
- 若 `connect` 失敗，請一起檢查 server、bootstrapper 與 target process 的 bitness。
- Codex 使用 STDIO transport，因此請保持 `stdout` 乾淨。
- 如果目標 app 是 elevated，請以系統管理員權限啟動 Codex 或其宿主終端機。非系統管理員權限的 Codex host 通常看得到 process，但無法真正控制 elevated target。
