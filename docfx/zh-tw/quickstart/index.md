# 5 分鐘快速開始

這份 quickstart 以公開發布前流程為準：使用本機產生且已驗證的 release package、將安裝後的執行檔註冊到 MCP client，最後用 scene-first 工作流驗證第一個 live WPF session。

## 先決條件

- Windows 10 以上
- 與 MCP server 使用同一個使用者帳號執行的 WPF 應用程式
- 已確認要安裝的 package 架構為 `x64`、`x86` 或 `arm64`
- 已審查 WPF target 的 exact local absolute executable path，並將它加入 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`；未設定或 malformed allowlist 會在 `connect()` attach 前 fail closed

## 先確認架構規則

Raw injection/bootstrapper fallback 必須符合架構。需要 zero-instrumentation attach 時，請安裝與 target process 相同 bitness 的 package。

SDK-hosted reuse 透過 named pipes 通訊；target-side host 已啟動後，不要求 server process 與 target process bitness 相同。

- `x64` target -> 安裝並執行 `x64` 套件
- `x86` target -> 安裝並執行 `x86` 套件
- `arm64` target -> 安裝並執行 `arm64` 套件

## Step 1：選擇安裝路徑

### GitHub Release assets 存在後的公開 HTTPS installer

待該版本的 GitHub Release assets 與 sidecar 都已上傳後，可使用公開一行安裝命令：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

這個 HTTPS alias 會解析到已審查的 `scripts/online-installer.ps1` entrypoint。升級為公開安裝入口前，必須先有對應的 GitHub Release assets：`release_<version>_win-<arch>.zip`、`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json` 與 `release-evidence.json`。

### GitHub pre-release E2E online installer path

正式公開 release 前的外部 E2E，請從公開 installer alias 下載已審查的 online installer，並在不 clone repository 的情況下安裝最新 GitHub pre-release：

```powershell
$e2eRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-mcp-e2e'
New-Item -ItemType Directory -Force -Path $e2eRoot | Out-Null
$installerPath = Join-Path $e2eRoot 'online-installer.ps1'
$installerDownload = @{
    Uri = 'https://installer.wpf-mcptools.evanlau1798.com/'
    OutFile = $installerPath
}
if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('UseBasicParsing')) {
    $installerDownload.UseBasicParsing = $true
}
Invoke-WebRequest @installerDownload
$installRoot = Join-Path $e2eRoot 'installed-wpf-devtools'
$workingRoot = Join-Path $e2eRoot 'installer-work'
powershell -ExecutionPolicy Bypass -File $installerPath -Version latest -Prerelease -Architecture x64 -Client other -InstallRoot $installRoot -WorkingRoot $workingRoot -NonInteractive -Force -OutputJson
```

Pre-release E2E 需要 GitHub pre-release 內含對應 package archive 與 sidecars，包含 `release-assets.json`、`SHA256SUMS.txt`、`release-sbom.spdx.json` 與 `release-evidence.json`。signed `Release` packaging 仍需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。validation-only E2E 範例使用 `-Client other`，讓 installer 只輸出 artifact-only registration，不修改真實 MCP client 設定。

若要做真正的本機 `connect()` E2E，請啟動安裝後的 packaged executable：`<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe`。直接從 source tree 啟動 server 可以驗證 `initialize`、`tools/list`、resources 與 STDIO framing，但不能證明 raw injection 所需的 packaged `bin\inspectors` 與 `bin\bootstrapper` sidecar layout。完整 64-tool 驗證應低於預設每分鐘 300 request 的 rate limit，或只在該本機 E2E session 設定 `WPFDEVTOOLS_RATE_LIMIT_RPM=10000`。

如果要直接執行 MCP STDIO smoke test，請每行送出一個 newline-delimited JSON (NDJSON) JSON-RPC message。不要對此 server 的 STDIO transport 使用 `Content-Length` framed messages。large `tools/list` schema payload 很大，請使用 Python、PowerShell 7 或 .NET 等真正 JSON parser，不要使用 regex 或固定行長假設。

Minimal NDJSON smoke sequence:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"external-e2e","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"connect","arguments":{}}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_ui_summary","arguments":{"depthMode":"semantic"}}}
```

### 已審查的本機 package 安裝

請先審查 `scripts/online-installer.ps1` 作為正式來源。這支 installer 可安裝本機 package archive、在解壓前驗證 archive integrity，然後透過已審查的 installer/helper flow 安裝解壓出的 packaged payload。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

預設互動流程會詢問 release 版本，以目前電腦架構（`x64`、`x86` 或 `arm64`）作為預設架構，接著詢問要產生哪一種 MCP client registration。省略 `-Architecture` 時，installer 會偵測系統架構；只有在刻意安裝不同套件時才傳入 `-Architecture`。

指定 client 的自動化範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

### 手動 release package

1. 使用本機產生的 package，或等 public endpoint smoke check 通過後，再開啟 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases)。
2. 下載 `release_<version>_win-x64.zip`、`release_<version>_win-x86.zip` 或 `release_<version>_win-arm64.zip`，並一併下載 `SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json`。
3. 解壓前，先用 `SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json` 驗證 archive。
4. 解壓縮。
5. 在解壓後的資料夾執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata，`release-sbom.spdx.json` 用於 release asset SBOM。SBOM sidecar 是 asset-level release archive inventory，not a full package/dependency SBOM。Production payload signature verification 仍需要獨立的 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`；相鄰 sidecar 只能證明 archive provenance，不能取代 signer trust。`WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 additional constraint。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

### 本機腳本執行

如果你偏好 package-local 安裝，而不是上述 script-first 路徑，請改用上面的手動 release package 流程。

## Step 2：確認安裝後的執行檔位置

安裝完成後，若沒有可沿用的既有 install root，回退執行檔路徑會是：

```text
%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## Step 3：註冊安裝後的執行檔

線上安裝腳本與手動 package 安裝都會在下列位置產生可直接複製的 client registration artifact：

```text
<InstallRoot>\<arch>\client-registration\
```

如果未指定 `-InstallRoot`，installer 會先沿用最後一個仍有 live install evidence 的 install root；只有在沒有可沿用路徑時，才會回退到 `%APPDATA%\WpfDevToolsMcp`。請把產生的 `client-registration` artifact 視為解析後路徑的真源。

如果你傳入 `-InstallRoot`，payload 與 client registration artifact 會寫在該明確 root 底下，但 installer state tracking 仍位於 `%APPDATA%\WpfDevToolsMcp\installer-state.json`。這個 state file 用來記錄最後的 live install evidence，供後續 installer 執行時重用。

如果你要手動註冊，請一律指向 `client-registration` artifact 所對應的已安裝 `wpf-devtools-<arch>.exe`，不要使用 source tree 的 `dotnet run`。

## Step 4：啟動或保持 WPF target 正在執行

server 只能檢查 live WPF process。先啟動目標應用程式，再啟動 MCP client。

如果你擁有 target app source code，請先參考 [SDK-hosted Inspector quickstart](sdk-hosted-inspector.md)，再考慮 raw injection。raw injection remains the fallback path for zero-instrumentation diagnostics。

## Step 5：驗證第一個 session

第一次呼叫 `connect()` 前，先明確設定本次診斷 session 的 policy profile：

```powershell
$targetExe = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $targetExe
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $targetExe
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

`WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 控制哪些 process metadata 與 connection target 可見。當 session 需要 raw injection，而不是重用已執行的 SDK-hosted Inspector 時，也必須設定 `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS`。進行 scene、form、tree、binding、DP 或 state reads 前，需要 `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`，否則包含 UI text 或 runtime value 的讀取會 fail closed。

在 MCP client 中使用以下順序：

1. 確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含 target 的 exact local absolute executable path。
2. `connect`
3. 如果 auto-discovery 回報多個候選，先呼叫 `get_processes(windowFilter)`，再重試 `connect(processId)`
4. `get_ui_summary(depthMode: "semantic")`
5. 只有當 summary 還不夠時，才使用 `get_visual_tree`，或在已取得具體 `elementId` 後使用 `get_element_snapshot(elementId)`
6. 只有需要明確健康檢查時才呼叫 `ping`
7. 每次診斷、互動或 mutation 後，優先遵循 `navigation.recommended`；若 client 尚未呈現 navigation，則把 `nextSteps` 當成相容欄位

互動或 mutation 前，最小有效 rollback guard 範例：

```powershell
$env:WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS = 'true'

tools/call capture_state_snapshot @{
    includeFocus = $true
    snapshotName = 'first-session-focus'
}
```

`capture_state_snapshot` 會刻意要求至少提供 `propertyNames`、`viewModelPropertyNames` 或 `includeFocus` 其中之一；空的 argument object 會回傳結構化 `MissingRequiredParameter` error。

健康的首次執行徵象：

- `connect()` 在 target 已 allowlist、可使用 injection 或已暴露 SDK-hosted Inspector，且只有一個可見 WPF target 時成功
- 若存在多個 target，`get_processes(windowFilter)` 能回傳正確候選清單
- `get_ui_summary` 能穩定回傳 root scene 的語意摘要

## 給 AI client 的快速提示詞

以下保留英文，是為了方便直接貼給 client：

```text
After WPFDEVTOOLS_MCP_ALLOWED_TARGETS and WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS include the running WPF app's exact local absolute executable path, and WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true is set for scene reads, connect to it, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## 想看更深入的安裝說明？

- [AI Agent Client 快速開始](ai-agent-clients.md)
- [SDK-Hosted Inspector](sdk-hosted-inspector.md)
- [部署指南](../production/deployment.md)
- [Release Layout](../production/release-layout.md)
