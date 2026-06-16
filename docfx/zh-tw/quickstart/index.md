# 5 分鐘快速開始

這是 Windows 上 production onboarding 的主要路徑。

## 需求

- Windows。
- 支援的 MCP client：Claude Code、OpenAI Codex/Codex CLI、Cursor、VS Code、Visual Studio、Claude Desktop，或 artifact-only client。
- 已審查且允許檢查的 WPF target executable path。
- 正式 package 所需的 .NET runtime 條件。

## 安裝

在第一個 stable GitHub Release 發布前，請使用 preview pre-release installer：

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

Installer 會偵測 host architecture、詢問 MCP client、安裝 packaged executable，並在下列位置產生 client registration artifact：

```text
<InstallRoot>\<arch>\client-registration\
```

在 stable release assets 與 anonymous endpoint smoke checks 通過前，不要使用 default stable `latest` channel。

安裝後 server path 通常是：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 手動驗證 package 安裝

若你已取得已審查的 release archive，請使用此路徑。

1. 下載 `release_<version>_win-<arch>.zip`。
2. 將這些 sidecar 放在 archive 旁邊：`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json`、`package-sbom.spdx.json` 與 `release-evidence.json`。
3. 用 `SHA256SUMS.txt` 驗證 archive hash，並用 `release-assets.json` 驗證 release metadata。
4. Review 兩份 SBOM：`release-sbom.spdx.json` 用於 release assets，`package-sbom.spdx.json` 用於 package/dependency/payload contents。
5. 驗證 release evidence 與 signer pin policy。Payload 簽章驗證仍需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。
6. 解壓後執行：

   ```powershell
   .\run.bat
   ```

## 註冊 client

請以產生的 `client-registration` artifact 作為最終 command 或 JSON path 的真源。

| Client | 指南 |
| --- | --- |
| Claude Code | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | [OpenAI Codex 與 Codex CLI](openai-codex.md) |
| Claude Desktop | [Claude Desktop](claude-desktop.md) |
| Cursor、VS Code、Visual Studio | [Cursor、VS Code 與 Visual Studio](cursor-vscode.md) |
| 一般 client matrix | [AI Agent Client](ai-agent-clients.md) |
| 你擁有 target app source | [SDK-Hosted Inspector](sdk-hosted-inspector.md) |

## 連線到 WPF target

第一次 tool call 前，請 allowlist 已審查 WPF target 的 exact local absolute executable path。第一次 scene summary 也需要 sensitive-read approval：

```powershell
$target = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

接著啟動 MCP client 並呼叫：

1. `connect`
2. `get_active_process`
3. `get_ui_summary`，並設定 `depthMode: "semantic"`
4. response 的 `navigation.recommended` 建議的 focused diagnostic tool

只有在存在多個 WPF target，或需要連線前檢查 architecture/elevation details 時，才使用 `get_processes(windowFilter)`。

## 安全預設

Server 會 fail closed，除非對應 policy 明確啟用。

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 是每個 `connect()` target 的必要設定。
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` 允許 UI text、binding values、DependencyProperty values、event payloads、scene summaries 與 runtime state reads。
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 允許 `element_screenshot`。
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` 允許 `get_viewmodel`、`get_commands`、`get_datacontext_chain`、`modify_viewmodel` 與 `execute_command`；當 snapshot、batch operation 或 wait-after-mutation trigger 要求 ViewModel state 時也會套用同一 gate。
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` 允許已核准的 mutation、interaction、render measurement 與 session-state-consuming tools。
- `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` 是 raw injection fallback target 的必要設定。

## 疑難排解

- 如果 `connect()` 失敗，確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 使用 exact local absolute executable path。
- 如果 client 找不到 server，請複製 generated `client-registration` artifact，不要手動重打路徑。
- Raw injection/bootstrapper fallback 必須符合架構。SDK-hosted reuse 透過 named pipes 通訊，不需要 bitness-matched bootstrapper attach。
- 如果本機 execution policy 擋下已審查 script，請先檢查 script contents，並只在可信任 shell 中使用 process-scoped policy override。一般路徑應使用 `run.bat` 或 `pwsh -NoProfile -File`。
