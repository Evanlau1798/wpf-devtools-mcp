# AI Agent Client 快速開始

先安裝 WPF DevTools，再把安裝後的執行檔註冊到你偏好的 client。

如果由 AI agent 協助安裝，請先使用 side-effect-safe 的 [Agent 輔助安裝](../guides/agent-assisted-install.md) 契約，再執行 installer。

## 安裝真源

- Canonical source repository：目前這份 checkout
- Planned public repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Planned public releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)
- Online installer source: `scripts/online-installer.ps1`（維護者來源；請與你實際要執行的 release package 內版本相符的 `bin/install.ps1` 比對）

> **公開端點狀態：** Public release endpoints are not yet anonymously reachable。GitHub repository、Releases、latest-release API、raw installer URL 與 installer alias 都通過匿名 smoke check 前，請使用本機產生且已驗證的 release package 或 source checkout，不要執行遠端一行安裝命令。

建議的本機 package 安裝路徑：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

這支已審查的 installer 會在解壓前驗證 archive integrity，然後透過已審查的 installer/helper flow 安裝解壓出的 packaged payload。

指定 client 的範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

手動 package 的替代路徑：

1. 使用本機產生的 package，或等 public endpoint smoke check 通過後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載 `release_<version>_win-<arch>.zip`、`SHA256SUMS.txt` 與 `release-assets.json`。
2. 解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。
3. 解壓縮套件。
4. 執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata。如果解壓後的套件旁已沒有原始且已驗證的 archive 與 sidecar，請在執行 `run.bat` 前設定 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 作為必要的 thumbprint trust root；`WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 additional constraint。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

Package-local 替代路徑：

```text
下載對應的 release_<version>_win-<arch>.zip，解壓後執行 run.bat。
```

所有支援的 setup 路徑最後都應該啟動安裝後的執行檔，而不是 source tree 內的命令。

在沒有可沿用的既有 install root 時，回退路徑範例：

```text
%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

線上安裝腳本與手動 package 安裝都會在下列位置產生 client-specific registration artifact：

```text
<InstallRoot>\<arch>\client-registration\
```

如果未指定 `-InstallRoot`，installer 會先沿用最後一個仍有 live install evidence 的 install root；只有在沒有可沿用路徑時，才會回退到 `%APPDATA%\WpfDevToolsMcp`。請把產生的 `client-registration` artifact 視為最終輸出路徑的真源。

第一次連線前，請確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含已審查 target 的 exact local absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。

## 建議選擇

| Client | 最適合的情境 | 註冊方式 | 指南 |
| --- | --- | --- | --- |
| Claude Code | 終端機導向的 agent workflow | installer 產生的 command | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | OpenAI CLI 與 agent workflow | installer 產生的 command | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Cursor | 編輯器或 Cursor CLI workflow | installer 產生的 JSON | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| Claude Desktop | 桌面聊天 workflow | installer 產生的 JSON config | [Claude Desktop](claude-desktop.md) |
| VS Code / Visual Studio | 編輯器導向 workflow | installer 產生的 JSON config | [VS Code 與 Visual Studio](cursor-vscode.md) |

## 第一次驗證流程

不論你選哪個 client，都建議依序驗證：

1. `connect`
2. 如果 auto-discovery 回報多個候選，先呼叫 `get_processes(windowFilter)`，再重試 `connect(processId)`
3. `get_ui_summary(depthMode: "semantic")`
4. 只有當 summary 還不夠時，才使用 `get_visual_tree`，或在已取得具體 `elementId` 後使用 `get_element_snapshot(elementId)`
5. 只有需要明確存活檢查時才呼叫 `ping`
6. 每次診斷、互動或 mutation 後，優先遵循 `navigation.recommended`，並把 `nextSteps` 視為舊版 client 的相容欄位

## WPF 特有提醒

- MCP server 必須跑在 Windows 上。
- 請保持 `stdout` 乾淨，因為 transport 是 STDIO。
- server 與 bootstrapper 位元數必須和 target process 一致。
- 呼叫 `connect` 前，先用已審查 target 的 exact local absolute executable path 設定 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`。
- `client-registration` 產物是最可靠的 copy-paste 真源。
- 在需要之前，先用 scene-level 工具，不要太早展開整棵 tree 或索取完整 screenshot。
- 如果工具回應已提供 `navigation.recommended` 或 `nextSteps`，請先遵循這個執行期 guidance，再決定是否補其他工具。

下一步：選擇你要使用的 client-specific 指南。
