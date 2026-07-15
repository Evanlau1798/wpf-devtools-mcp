# 5 分鐘快速開始

這是 Windows 上最短的 production onboarding 路徑。它會帶你從安裝走到第一個有用的 WPF scene summary，再連到手動 package 驗證、client-specific registration 與 production hardening 的 deeper pages。

## 這個流程會做什麼

1. 安裝 packaged MCP server。
2. 從 generated `client-registration` artifacts 註冊一個 supported MCP client。
3. 用 exact local absolute executable path allowlist 一個已審查的 WPF target。
4. 在必要 policy gates 設定後驗證第一次 session。

若要做進階 release archive verification，請跳過 happy path，改看 [手動驗證安裝](manual-install.md)。

## 需求

- Windows。
- 支援的 MCP client：Claude Code、OpenAI Codex/Codex CLI、Cursor、VS Code、Visual Studio、Claude Desktop，或 artifact-only client。
- 已審查且允許檢查的 WPF target executable path。
- 正式 package 所需的 .NET runtime 條件。

## 安裝 server

安裝最新 stable release：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

Installer 會偵測 host architecture、詢問 MCP client、安裝 packaged executable，並在下列位置產生 client registration artifact：

```text
<InstallRoot>\<arch>\client-registration\
```

一般安裝不需要手動下載 release ZIP 或 release sidecar。

安裝後 server path 通常是：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

ARM64 發行檔可作為 preview asset 提供，但目前不保證穩定性，因為尚無可行的 Windows-on-ARM runtime 驗證硬體。

## 只有需要時才安裝指定 pre-release

```powershell
$version = 'v1.0.0-beta.69'
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version $version -Prerelease
```

請將範例值替換為實際選定的 public pre-release tag。

## 解除安裝或復原自訂安裝

先停止正在使用 server 的 client，再沿用安裝時相同的 exact custom install root。`uninstall` 會移除單一 client registration；`full-uninstall` 會移除所有偵測到的 registrations 與 installer-owned server locations：

```powershell
$installRoot = '<exact-install-root>'
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Action uninstall -Client '<client-id>' -InstallRoot $installRoot -NonInteractive -Force -OutputJson
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Action full-uninstall -InstallRoot $installRoot -NonInteractive -Force -OutputJson
```

## 註冊 client

請以產生的 `client-registration` artifact 作為最終 command 或 JSON path 的真源。當 artifact 已含 installed executable path 時，不要手動重打路徑。

| Client | 指南 |
| --- | --- |
| Claude Code | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Claude Desktop | [Claude Desktop](claude-desktop.md) |
| Cursor、VS Code、Visual Studio | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| 一般 client matrix | [AI Agent Client](ai-agent-clients.md) |
| 你擁有 target app source | [SDK-Hosted Inspector](sdk-hosted-inspector.md) |

## Allowlist 一個 WPF target

第一次 tool call 前，請 allowlist 已審查 WPF target 的 exact local absolute executable path。第一次 scene summary 也需要 sensitive-read approval：

```powershell
$target = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

`WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 是每個 `connect()` target 的必要設定。`WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` 只有在允許 raw injection fallback 時才需要，但這裡一併列出，可避免 target 無法 host SDK inspector 時的常見 first-run confusion。

## 驗證第一次 session

啟動 MCP client 並呼叫：

1. `connect`
2. `get_active_process`
3. `get_ui_summary`，並設定 `depthMode: "semantic"`
4. response 的 `navigation.recommended` 建議的 focused diagnostic tool

只有在存在多個 WPF target，或需要連線前檢查 architecture/elevation details 時，才使用 `get_processes(windowFilter)`。

穩定的下一步 recipe 請看 [常見工作流程](../guides/common-workflows.md)。`navigation.recommended`、`navigation.alternatives`、`prefetchTools`、`contextRefs`、`nextSteps` 與 `structuredContent` 等欄位請看 [MCP Contracts 與 Navigation](../reference/mcp-contracts.md)。

若要先從 installed extension packs 建立 WPF UI，再檢查啟動後的 app，請從 [UI Composer 工具](../reference/tools/ui-composer.md) 開始。

## 用白話理解安全預設

Server 會 fail closed，除非對應 policy 明確啟用。

| Capability | 預設 | 啟用方式 | 常見第一個 tool |
| --- | --- | --- | --- |
| 連線到 target | allowlist 前 blocked | `WPFDEVTOOLS_MCP_ALLOWED_TARGETS=<exact local absolute executable path>` | `connect` |
| 讀取 UI text 與 runtime state | Blocked | `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` | `get_ui_summary` |
| 擷取 pixel | Blocked | `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` | `element_screenshot` |
| 檢查 ViewModel state | Blocked | `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` | `get_viewmodel` |
| mutation 或 UI interaction | Blocked | `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` | `click_element`、`batch_mutate` |
| Raw injection fallback | Blocked | `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS=<exact local absolute executable path>` | `connect` |

`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` 允許 `get_viewmodel`、`get_commands`、`get_datacontext_chain`、`modify_viewmodel` 與 `execute_command`；當 snapshot、batch operation 或 wait-after-mutation trigger 要求 ViewModel state 時也會套用同一 gate。

## 進階路徑：手動驗證 package 安裝

只有在你已取得已審查的 release archive 時，才使用此路徑。解壓前需要 `release_<version>_win-<arch>.zip`，以及 `SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json` 與 `package-sbom.spdx.json`。

Trust mode 必須明確：`Signed` package 需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`；`ReleaseChecksumOnly` beta package 需要 GitHub Release sidecars 或 trusted metadata directory 提供 SHA256 release metadata。

```powershell
$archive = '.\release\release_<version>_win-<arch>.zip'
$metadata = '.\release'
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) `
  -PackageArchivePath $archive `
  -TrustedReleaseMetadataDirectory $metadata
```

Portable checksum-only validation 只有在 extracted package 與原始 archive 及 `SHA256SUMS.txt` 放在同一目錄，或 `WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY` 指向該 metadata directory 時，才使用 package-local `run.bat` 或 `bin\wpf-devtools-<arch>.exe`。

請依照 [手動驗證安裝](manual-install.md) 完成 sidecar checks、trust-mode decision、本機 archive install command 與 portable package rules。

## 疑難排解

- 如果 `connect()` 失敗，確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 使用 exact local absolute executable path，且沒有 malformed entries。
- 如果 client 找不到 server，請複製 generated `client-registration` artifact，不要手動重打路徑。
- 如果手動 package 安裝後 `connect()` 回傳 `SecurityError: Security verification failed`，請先確認 `<InstallRoot>\<arch>\current\bin\` 下的 installed path。Portable package 檢查請見 [手動驗證安裝](manual-install.md)。
- Raw injection/bootstrapper fallback 必須符合架構。SDK-hosted reuse 透過 named pipes 通訊，不需要 bitness-matched bootstrapper attach。
- 如果本機 execution policy 擋下已審查 script，請先檢查 script contents，並只在可信任 shell 中使用 process-scoped policy override。一般路徑應使用 `run.bat` 或 `pwsh -NoProfile -File`。
