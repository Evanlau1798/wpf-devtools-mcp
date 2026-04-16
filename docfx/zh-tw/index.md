# WPF DevTools MCP Server 繁體中文文件

[English version](../index.md)

WPF DevTools MCP Server 是一個只支援 Windows 的 Model Context Protocol 伺服器。它透過注入執行中的 in-process inspector，讓你直接檢查與操作 WPF 應用程式，涵蓋 Binding 診斷、Dependency Property 優先順序、scene-level 摘要、MVVM 檢視、Routed Event 追蹤、Layout 除錯與受控 runtime mutation 等場景。

## 正式來源

- Repository: [https://github.com/Evanlau1798/wpf-devtools-mcp](https://github.com/Evanlau1798/wpf-devtools-mcp)
- Releases: [https://github.com/Evanlau1798/wpf-devtools-mcp/releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)
- Online installer source: [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)
- Release packaging source: [scripts/tools/packaging/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/tools/packaging/Publish-Release.ps1)
- Installed-layout sources: [scripts/installer/Installer.Actions.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/installer/Installer.Actions.ps1), [scripts/installer/Installer.Registration.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/installer/Installer.Registration.ps1)

`scripts/` 是安裝與 release 行為的唯一真源。這個 DocFX 站台只負責說明，不定義腳本本身。

## 安裝路徑

### 線上安裝腳本路徑

請先審查正式來源：[scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)

預設一鍵安裝：

```powershell
$architecture = 'x64'
$version = (Invoke-RestMethod 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest').tag_name.TrimStart('v')
$assetName = "release_${version}_win-$architecture.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wpf-devtools-install-" + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $tempRoot $assetName
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
Invoke-WebRequest "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force
powershell -ExecutionPolicy Bypass -File (Join-Path $tempRoot 'bin\install.ps1')
```

指定 client 的範例：

```powershell
$architecture = 'x64'
$version = (Invoke-RestMethod 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest').tag_name.TrimStart('v')
$assetName = "release_${version}_win-$architecture.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wpf-devtools-install-" + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $tempRoot $assetName
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
Invoke-WebRequest "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force
powershell -ExecutionPolicy Bypass -File (Join-Path $tempRoot 'bin\install.ps1') -Version latest -Architecture $architecture -Client claude-code -NonInteractive -Force -OutputJson
```

### 手動 release package 路徑

1. 從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載 `release_<version>_win-x64.zip`、`release_<version>_win-x86.zip` 或 `release_<version>_win-arm64.zip`。
2. 解壓縮套件。
3. 在解壓後的資料夾內執行 `run.bat`。

## 依需求選擇入口

| 我想要... | 從這裡開始 |
| --- | --- |
| 用英文閱讀完整文件 | [English version](../index.md) |
| 快速安裝並驗證第一個 session | [5 分鐘快速開始](quickstart/index.md) |
| 比較不同 AI client 的註冊方式 | [AI Agent Client 快速開始](quickstart/ai-agent-clients.md) |
| 從 Claude Code 使用這個 server | [Claude Code 快速開始](quickstart/claude-code.md) |
| 從 OpenAI Codex 或 Codex CLI 使用這個 server | [OpenAI Codex 與 Codex CLI 快速開始](quickstart/openai-codex.md) |
| 從 Claude Desktop 使用這個 server | [Claude Desktop 快速開始](quickstart/claude-desktop.md) |
| 從 VS Code 或 Visual Studio 使用這個 server | [VS Code 與 Visual Studio 快速開始](quickstart/cursor-vscode.md) |
| 了解 agent-safe 工作流與回應契約 | [AI Agent 指南](guides/ai-agent-guide.md) |
| 查看部署與 package layout 契約 | [部署指南](production/deployment.md) |
| 了解 runtime 與 injection 限制 | [Bootstrap 與 Injection](production/bootstrap-and-injection.md) |
| 參與程式、測試或文件貢獻 | [貢獻指南](contributors/index.md) |

## 這個 server 的差異化價值

- **WPF 原生可見性**：可直接檢查 `BindingOperations`、Dependency Property value source、Namescope、Template、Routed Event 與 Layout 狀態，這些資訊不是一般 out-of-process 工具能完整取得的。
- **面向 agent 的契約**：tool metadata 由程式碼定義，scene-first 工作流有明確文件，runtime follow-up guidance 透過 `navigation` 與相容欄位 `nextSteps` 提供。
- **正式環境等級的診斷能力**：目前能力面已包含 compact binding triage、state snapshot、sequential batch mutation、buffered runtime event drain 與 scene-level summary。
- **穩定的封裝與安裝流程**：repo 內含 release packaging、installer 建置、可選安全設定與適合公開發佈的驗證步驟。

## 目前可以做什麼

- 探索執行中的 WPF process 並連接到正確 target。
- 以 `get_ui_summary`、`get_element_snapshot`、`get_form_summary` 等 scene-level 工具作為第一步。
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
- **安全姿態**：authentication 與 TLS 為 opt-in；Debug 與 Release build 在 DLL 驗證策略上不同。

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
