# OpenAI Codex 與 Codex CLI 快速開始

如果你要從 Codex workflow 使用已安裝的 WPF DevTools server，請使用這份指南。

## 1. 安裝 Codex CLI

```powershell
npm install -g @openai/codex
```

## 2. 安裝 WPF DevTools

GitHub Release assets 存在後的公開安裝命令：

```powershell
irm https://wpf-mcptools.evanlau1798.com | iex
```

這個 HTTPS alias 會解析到 `scripts/online-installer.ps1`；只有在該版本已具備 GitHub Release assets 與 sidecar 後才提升為公開 onboarding 路徑：`release_<version>_win-<arch>.zip`、`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json` 與 `release-evidence.json`。

建議的本機 package 安裝路徑：

1. 先審查 `scripts/online-installer.ps1`，把它當成正式來源入口。
2. 使用已審查的 installer 安裝已驗證的本機 package archive。

範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client codex -NonInteractive -Force -OutputJson
```

package-local 回退路徑：

1. 使用本機產生的 package，或等 GitHub Release assets 存在後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載對應架構的 `release_<version>_win-<arch>.zip`、`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json` 與 `release-evidence.json`。
2. 解壓前，先用 `SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json` 驗證 archive。
3. 解壓縮套件。
4. 執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata，`release-sbom.spdx.json` 用於 release asset SBOM。SBOM sidecar 是 asset-level release archive inventory，not a full package/dependency SBOM。Production payload signature verification 仍需要獨立的 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`；相鄰 sidecar 只能證明 archive provenance，不能取代 signer trust。`WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 additional constraint。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

對 `claude-code` 與 `codex`，elevated CLI registration 會刻意封鎖 PATH-based CLI discovery 與環境變數提供的 command path。請優先用 `WPFDEVTOOLS_SKIP_ELEVATION=1` 留在目前 shell 完成註冊，或在安裝後手動註冊。

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

以下保留英文，是為了方便直接貼給 client：

使用此 prompt 前，請確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含執行中 WPF app 的 exact local absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。

## 5. 第一個實用 prompt

以下保留英文，是為了方便直接貼給 client：

```text
After WPFDEVTOOLS_MCP_ALLOWED_TARGETS includes the running WPF app's exact local absolute executable path, connect to it, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 注意事項

- 即使你的編輯器或 agent workflow 跨環境，MCP server 本體仍需在 Windows 執行。
- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含已審查 target 的 exact local absolute executable path 後，一般情況先從 `connect()` 開始；只有 auto-discovery 出現多個候選，或你想先看明確 target metadata 時，才使用 `get_processes(windowFilter)`。
- 在 tree-heavy inspection 前，優先使用 scene-level 工具。
- 每次診斷、互動或 mutation 後，優先遵循 `navigation.recommended`，並把 `nextSteps` 視為相容欄位。
- 如果你已經知道下一步工具，且希望回應更精簡，具備額外 optional args 傳遞能力的 client 可在 `get_binding_errors` 呼叫傳入 `navigation=false`；schema-driven client 可以在這個工具上依賴這個 opt-out，因為它今天已經公告在 tool schema 中，但不應假設其他工具今天也有公開這個參數。
- 若 `connect` 失敗，請一起檢查 server、bootstrapper 與 target process 的 bitness。
- Codex 使用 STDIO transport，因此請保持 `stdout` 乾淨。
- 如果目標 app 是 elevated，請以系統管理員權限啟動 Codex 或其宿主終端機。非系統管理員權限的 Codex host 通常看得到 process，但無法真正控制 elevated target。
