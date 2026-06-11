# WPF DevTools MCP Server 繁體中文文件

[English version](../index.md)

WPF DevTools MCP Server 是一個只支援 Windows 的 Model Context Protocol 伺服器。它透過注入執行中的 in-process inspector，讓你直接檢查與操作 WPF 應用程式，涵蓋 Binding 診斷、Dependency Property 優先順序、scene-level 摘要、MVVM 檢視、Routed Event 追蹤、Layout 除錯與受控 runtime mutation 等場景。

## 正式來源

- Canonical source repository：目前這份 checkout
- Planned public repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Planned public releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)
- Online installer source: `scripts/online-installer.ps1`（維護者來源；執行 published package 前，請先比對版本相符的 release artifact）
- Release packaging source: `scripts/tools/packaging/Publish-Release.ps1`
- Installed-layout sources: `scripts/installer/Installer.Actions.ps1`, `scripts/installer/Installer.Registration.ps1`

`scripts/` 是安裝與 release 行為的唯一真源。這個 DocFX 站台只負責說明，不定義腳本本身。

## 安裝路徑

### GitHub Release assets 存在後的公開 HTTPS installer

該版本的 GitHub Release assets 與 sidecar 都存在後，可使用公開一行安裝：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

這個 HTTPS alias 會解析到已審查的 `scripts/online-installer.ps1` entrypoint。升級為公開安裝入口前，必須先有對應的 GitHub Release assets：`release_<version>_win-<arch>.zip`、`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json` 與 `release-evidence.json`。

### 已驗證的本機 package 安裝

請先審查維護者來源：`scripts/online-installer.ps1`。這支已審查的 installer 可安裝本機 package archive、在解壓前驗證 archive integrity，然後透過已審查的 installer/helper flow 安裝解壓出的 packaged payload。

本機 package 範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

預設互動流程會詢問 release 版本，以目前電腦架構（`x64`、`x86` 或 `arm64`）作為預設架構，接著詢問要產生哪一種 MCP client registration。省略 `-Architecture` 時，installer 會偵測系統架構；只有在刻意安裝不同套件時才傳入 `-Architecture`。

指定 client 的自動化範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

Repository 內的 entrypoint 仍只是 bootstrap 層；實際安裝動作會透過已審查的 installer/helper flow 使用已驗證且解壓出的 package payload。

### 手動 release package 路徑

1. 使用本機產生的 package，或等 public endpoint smoke check 通過後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載 `release_<version>_win-x64.zip`、`release_<version>_win-x86.zip` 或 `release_<version>_win-arm64.zip`，並同時下載 `SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json`。
2. 在解壓前先用 release provenance sidecars 驗證下載的 archive。確認資產雜湊同時符合 `SHA256SUMS.txt`、`release-assets.json` 內對應 asset 的紀錄，以及 `release-sbom.spdx.json` 的 release asset SBOM sidecar。已審查的 online installer 會自動完成這個驗證；手動路徑不會。
3. 執行 package-local installer 時，請把已驗證的 release zip、`SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json` 一起保留在解壓資料夾的父目錄。SBOM sidecar 是 asset-level release archive inventory，not a full package/dependency SBOM。Production payload signature verification 仍需要獨立的 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`；相鄰 sidecar 只能證明 archive provenance，不能取代 signer trust。`WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 additional constraint。
4. 解壓縮套件。
5. 在解壓後的資料夾內執行 `run.bat`。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

## 依需求選擇入口

| 我想要... | 從這裡開始 |
| --- | --- |
| 用英文閱讀完整文件 | [English version](../index.md) |
| 快速安裝並驗證第一個 session | [5 分鐘快速開始](quickstart/index.md) |
| 比較不同 AI client 的註冊方式 | [AI Agent Client 快速開始](quickstart/ai-agent-clients.md) |
| 從 Claude Code 使用這個 server | [Claude Code 快速開始](quickstart/claude-code.md) |
| 從 OpenAI Codex 或 Codex CLI 使用這個 server | [OpenAI Codex 與 Codex CLI 快速開始](quickstart/openai-codex.md) |
| 從 Claude Desktop 使用這個 server | [Claude Desktop 快速開始](quickstart/claude-desktop.md) |
| 從 Cursor 使用這個 server | [Cursor 快速開始](quickstart/cursor-vscode.md) |
| 從 VS Code 或 Visual Studio 使用這個 server | [VS Code 與 Visual Studio 快速開始](quickstart/cursor-vscode.md) |
| 了解 agent-safe 工作流與回應契約 | [AI Agent 指南](guides/ai-agent-guide.md) |
| 查看部署與 package layout 契約 | [部署指南](production/deployment.md) |
| 了解 runtime 與 injection 限制 | [Bootstrap 與 Injection](production/bootstrap-and-injection.md) |
| 參與程式、測試或文件貢獻 | [貢獻指南](contributors/index.md) |

## 這個 server 的差異化價值

- **WPF 原生可見性**：可直接檢查 `BindingOperations`、Dependency Property value source、Namescope、Template、Routed Event 與 Layout 狀態，這些資訊不是一般 out-of-process 工具能完整取得的。
- **面向 agent 的契約**：tool metadata 由程式碼定義，scene-first 工作流有明確文件，runtime follow-up guidance 透過 `navigation` 與相容欄位 `nextSteps` 提供。
- **正式環境等級的診斷能力**：目前能力面已包含 compact binding triage、state snapshot、sequential batch mutation、buffered runtime event drain 與 scene-level summary。
- **穩定的封裝與安裝流程**：repo 內含 release packaging、installer 建置、預設 hardened 的 injection transport，以及適合公開發佈的驗證步驟。

## 目前可以做什麼

- 探索執行中的 WPF process 並連接到正確 target。
- 以可直接執行的 `get_ui_summary` 與 `get_form_summary` 作為 scene-level 第一步；取得具體 `elementId` 後，再使用 `get_element_snapshot(elementId)`。
- 使用 `get_binding_errors`、`get_affected_elements`、`get_bindings`、`get_binding_value_chain` 進行 binding 問題診斷。
- 分析 Dependency Property 優先順序、metadata、watch 與 timeout-bounded wait。
- 透過 `capture_state_snapshot`、`get_state_diff`、`restore_state_snapshot`、`batch_mutate` 執行安全的 runtime workflow。
- 使用 `trace_routed_events` 與 `drain_events` 追蹤或排空 runtime event buffer。

## 範圍與邊界

- **Transport**：正式發佈版本使用 STDIO MCP transport。
- **平台**：只支援 Windows。
- **Target UI stack**：只支援 WPF。
- **Injection 模型**：native bootstrapper 加 managed inspector。
- **持久化行為**：runtime mutation 不會寫回 XAML。
- **安全姿態**：以 injection 為基礎的 `connect` session 預設會使用持久化的本機 HMAC secret 與 named-pipe TLS。若需要 deterministic override，可透過 `WPFDEVTOOLS_AUTH_SECRET` 與 `WPFDEVTOOLS_CERT_DIR` 明確設定；若要重用 SDK-hosted Inspector，兩者都必須一致。Debug 與 Release build 在 DLL 驗證策略上不同。

## 架構總覽

```text
AI Client (Claude Code / Codex / Claude Desktop / Cursor / VS Code)
  -> MCP over STDIO
MCP Server (net8.0)
  -> named pipes with JSON messages and length-prefix framing
Native bootstrapper + managed inspector
  -> WPF Dispatcher and in-process APIs
Target WPF application
```

完整資料流請參考 [架構總覽](architecture/overview.md)，設計決策索引請參考 [ADR 索引](architecture/adrs/index.md)。

## 建議閱讀順序

1. [5 分鐘快速開始](quickstart/index.md)
2. [AI Agent Client 快速開始](quickstart/ai-agent-clients.md)
3. [AI Agent 指南](guides/ai-agent-guide.md)
4. [工具總覽](reference/tools/index.md)
5. [部署指南](production/deployment.md)
6. [Bootstrap 與 Injection](production/bootstrap-and-injection.md)
