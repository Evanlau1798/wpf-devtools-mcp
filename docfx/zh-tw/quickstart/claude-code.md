# Claude Code 快速開始

Claude Code 適合需要在終端機內以 agent workflow 使用 WPF DevTools 的情境。

## 1. 安裝 Claude Code

請依照 Claude Code 官方安裝指引 <https://docs.claude.com/en/docs/claude-code/overview>。

若選擇使用 Anthropic 的 PowerShell 安裝腳本，建議先下載再審查內容，再執行，以便稽核實際執行的指令：

```powershell
$installer = Join-Path $env:TEMP 'claude-install.ps1'
Invoke-WebRequest -Uri 'https://claude.ai/install.ps1' -OutFile $installer -UseBasicParsing
Get-Content $installer | Select-Object -First 60   # 審查後再執行
& $installer
```

> **資安提醒：** `irm <url> | iex` 一行式雖然方便，但會在未經檢視的情況下執行遠端程式；在未信任網路環境中，優先使用上述先下載再審查的流程。

## 2. 安裝 WPF DevTools

> **公開端點狀態：** Public release endpoints are not yet anonymously reachable。GitHub repository、Releases、latest-release API、raw installer URL 與 installer alias 都通過匿名 smoke check 前，請使用本機產生且已驗證的 release package 或 source checkout，不要執行遠端一行安裝命令。

建議的本機 package 安裝路徑：

1. 先審查 `scripts/online-installer.ps1`，把它當成正式來源入口。
2. 使用已審查的 installer 安裝已驗證的本機 package archive。

範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

package-local 回退路徑：

1. 使用本機產生的 package，或等 public endpoint smoke check 通過後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載對應架構的 `release_<version>_win-<arch>.zip`、`SHA256SUMS.txt` 與 `release-assets.json`。
2. 解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。
3. 解壓縮套件。
4. 執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata。如果解壓後的套件旁已沒有原始且已驗證的 archive 與 sidecar，請在執行 `run.bat` 前設定 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`（或 `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`），讓本機安裝流程仍會強制要求明確的 signer pin。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

對 `claude-code` 與 `codex`，elevated CLI registration 會刻意封鎖 PATH-based CLI discovery 與環境變數提供的 command path。請優先用 `WPFDEVTOOLS_SKIP_ELEVATION=1` 留在目前 shell 完成註冊，或在安裝後手動註冊。

如果 installer 不能重用先前仍有效的 live install root，且你也沒有傳入 `-InstallRoot`，回退用的 executable 路徑會是：

```text
%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
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

使用此 prompt 前，請確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含執行中 WPF app 的 exact absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。

## 5. 第一個實用 prompt

```text
After WPFDEVTOOLS_MCP_ALLOWED_TARGETS includes the running WPF app's exact absolute executable path, connect to it, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 6. 在 Claude Code 內做 discovery

- prompts 可能會以 `/mcp__wpf-devtools__connect_and_list_windows` 這類 slash commands 形式出現，但可攜的契約仍然是 prompt 名稱本身。
- resources 可能會以 `@wpf-devtools:capabilities` 與 `@wpf-devtools:limitations/elevated-targets` 這類引用形式出現，但可攜的契約仍然是 resource URI。
- 當 Claude Code 知道 server 已存在，但不容易挑到正確工具時，這些入口會比自由敘述更穩定。

## 注意事項

- server 必須執行在 Windows。
- 不要在 `wpf-devtools-x64.exe` 外層再包會污染 `stdout` 的啟動器。
- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含已審查 target 的 exact absolute executable path 後，一般情況先從 `connect()` 開始；只有 auto-discovery 回報多個候選，或你需要先拿到 target metadata 時才用 `get_processes(windowFilter)`。
- 在 tree-heavy inspection 前，優先使用 `get_ui_summary`、`get_element_snapshot` 或 `get_form_summary`。
- 每次診斷、互動或 mutation 後，優先遵循 `navigation.recommended`，並把 `nextSteps` 當成相容欄位。
- 如果你已經知道下一步工具，且希望回應更精簡，具備額外 optional args 傳遞能力的 client 可在 `get_binding_errors` 呼叫傳入 `navigation=false`；schema-driven client 可以在這個工具上依賴這個 opt-out，因為它今天已經公告在 tool schema 中，但不應假設其他工具今天也有公開這個參數。
- 若 `connect` 失敗，先一起檢查 server、bootstrapper 與 target 的 bitness。
- 如果目標 app 是 elevated，請以系統管理員權限啟動 Claude Code，讓它透過 STDIO 拉起的 MCP server 能在相同完整性等級下 attach。
