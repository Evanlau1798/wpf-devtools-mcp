# Claude Code 快速開始

Claude Code 是目前最直接的公開路徑，適合需要在終端機內以 agent workflow 使用 WPF DevTools 的情境。

## 1. 安裝 Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
```

## 2. 安裝 WPF DevTools

建議的公開安裝路徑：

1. 從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載對應架構的 `release_<version>_win-<arch>.zip`。
2. 解壓縮套件。
3. 執行 `run.bat`。

如果你偏好腳本驅動安裝，請先審查 [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) 再於本機執行。

範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -Force
```

安裝後的預設 executable 路徑是：

```text
%APPDATA%\WpfDevToolsMcp\x64\current\bin\wpf-devtools-x64.exe
```

## 3. 註冊 MCP server

可直接使用 `client-registration\claude-code.txt` 中的命令，或依照同樣格式改成你的實際安裝後絕對路徑：

```powershell
claude mcp add --transport stdio wpf-devtools -- "C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe"
```

若要做 project scope 註冊，可使用：

```powershell
claude mcp add --scope project --transport stdio wpf-devtools -- "C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe"
```

installer 也會輸出 `client-registration\claude-code.txt`。把它當成已審核的命令來源，因為它已經反映真實的 install root 與 architecture；若你要 project scope，請在命令上手動加上 `--scope project`。

## 4. 驗證註冊結果

```powershell
claude mcp list
```

## 5. 第一個實用 prompt

```text
Connect to the running WPF app, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 6. 在 Claude Code 內做 discovery

- prompts 會以 `/mcp__wpf-devtools__connect_and_list_windows` 這類 slash commands 形式出現。
- resources 會以 `@wpf-devtools:capabilities` 與 `@wpf-devtools:limitations/elevated-targets` 這類引用形式出現。
- 當 Claude Code 知道 server 已存在，但不容易挑到正確工具時，這些入口會比自由敘述更穩定。

## 注意事項

- server 必須執行在 Windows。
- 不要在 `wpf-devtools-x64.exe` 外層再包會污染 `stdout` 的啟動器。
- 一般情況先從 `connect()` 開始；只有 auto-discovery 回報多個候選，或你需要先拿到 target metadata 時才用 `get_processes(windowFilter)`。
- 在 tree-heavy inspection 前，優先使用 `get_ui_summary`、`get_element_snapshot` 或 `get_form_summary`。
- 如果你已經知道下一步工具，且希望回應更精簡，可在該次呼叫傳入 `navigation=false`。
- 若 `connect` 失敗，先一起檢查 server、bootstrapper 與 target 的 bitness。
- 如果目標 app 是 elevated，請以系統管理員權限啟動 Claude Code，讓它透過 STDIO 拉起的 MCP server 能在相同完整性等級下 attach。
