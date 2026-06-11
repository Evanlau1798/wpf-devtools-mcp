# Agent 輔助安裝

當 AI agent 協助使用者安裝 WPF DevTools MCP 時，請使用這份規格。Agent 必須先規劃、在使用者確認前避免副作用、驗證 release provenance，並且只呼叫已審查的 installer entrypoint。

## Agent 契約

- 先讀這份 guide 或 `AGENT_INSTALL.md`。
- 尚未安裝。
- 先提出 plan 並取得明確同意；任何修改前必須取得使用者確認。
- 只有在使用者確認 plan 且對應 GitHub Release assets 已存在後，才使用公開 online installer。
- 不要執行 moving-branch raw script 或未審查的遠端安裝命令。
- 不可要求 private key、PFX password、GitHub secrets 或 signing secrets。
- 不可儲存、列印、上傳或轉送 secrets。

支援的 client id：

- `claude-code`
- `codex`
- `cursor`
- `vscode`
- `visual-studio`
- `claude-desktop`
- `other`

## 必要使用者確認

執行 installer 或寫入 client configuration 前，請先要求使用者確認：

- Version，例如 `latest` 或具體 release tag。
- Architecture：`x64`、`x86` 或 `arm64`。
- Install root。
- Client registration target：`claude-code`、`codex`、`cursor`、`vscode`、`visual-studio`、`claude-desktop` 或 `other`。
- Cursor 要使用 global 或 project-local registration。
- Installer 是否可以更新既有 registration artifact。

## 偵測步驟

偵測階段必須是 read-only：

1. 確認 OS 是 Windows。
2. 執行 read-only plan command：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Action plan -OutputJson
   ```

3. 偵測 system architecture，並判斷 target WPF process 是否需要不同 architecture。
4. 偵測支援 client 的 CLI 或 config root。
5. 檢查是否有可沿用的既有 install root。
6. 回報結果並等待使用者確認。

`-Action plan` 會回報 supported clients、detected clients、architecture、default install root 與 mutation boundary。它不會下載、安裝、註冊 client，也不會寫入 installer state。

Plan output 範例：

```json
{
  "action": "plan",
  "contractVersion": 1,
  "platform": "windows",
  "version": "latest",
  "architecture": "x64",
  "client": "codex",
  "installRootDefault": "C:\\Users\\you\\AppData\\Roaming\\WpfDevToolsMcp",
  "preferredInstallRoot": "C:\\Users\\you\\AppData\\Roaming\\WpfDevToolsMcp",
  "fallbackInstallRoot": "C:\\Users\\you\\AppData\\Roaming\\WpfDevToolsMcp",
  "installRootSource": "default",
  "supportedClients": ["claude-code", "codex", "cursor", "vscode", "visual-studio", "claude-desktop", "other"],
  "detectedClients": [
    {
      "client": "codex",
      "available": true,
      "registrationStyle": "cli",
      "evidence": ["codex command"]
    },
    {
      "client": "other",
      "available": true,
      "registrationStyle": "artifact-only",
      "evidence": ["artifact-only fallback"]
    }
  ],
  "requiresUserConfirmationBeforeMutation": true,
  "mutatesFileSystem": false,
  "downloadsReleaseAssets": false,
  "runsClientRegistration": false,
  "mutationBoundary": "read-only discovery only; no download, install, registration, or filesystem mutation before user confirmation"
}
```

請把 plan 當成 evidence，而不是 permission。Agent 應先摘要選定的 `version`、`architecture`、`client`、`preferredInstallRoot`、detected client evidence，以及 read-only flags，再詢問使用者是否核准。`installRootSource` 會說明 preferred root 來自 explicit argument、default root，或 previous live install。

## Release 取得

解析 versioned release archive 名稱：

```text
release_<version>_win-<arch>.zip
```

取得相符 sidecar：

- `SHA256SUMS.txt`
- `release-assets.json`
- `release-sbom.spdx.json`
- `release-evidence.json`

對應 GitHub Release assets 存在後，公開 HTTPS installer entrypoint 是：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

這個 alias 會解析到 published release 對應的已審查 `scripts/online-installer.ps1` entrypoint。

正式公開 release 前的 pre-release E2E，請從公開 installer alias 下載已審查的 online installer，並在不 clone repository 的情況下安裝最新 GitHub pre-release：

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

Pre-release E2E 需要 GitHub pre-release 內含對應 package archive 與 sidecars，包含 `release-assets.json`、`SHA256SUMS.txt`、`release-sbom.spdx.json` 與 `release-evidence.json`。signed `Release` packaging 仍需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。validation-only E2E 請使用 `-Client other`，讓 installer 只輸出 artifact-only registration，不修改真實 MCP client 設定。

Direct MCP STDIO smoke tests 必須每行送出一個 newline-delimited JSON (NDJSON) JSON-RPC message。不要對此 server 的 STDIO transport 使用 `Content-Length` framed messages。large `tools/list` schema payload 很大，請使用 Python、PowerShell 7 或 .NET 等真正 JSON parser，不要使用 regex 或固定行長假設。

Minimal NDJSON smoke sequence:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"external-e2e","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"connect","arguments":{}}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_ui_summary","arguments":{"depthMode":"semantic"}}}
```

## 來源驗證

解壓或安裝前：

1. 使用 `SHA256SUMS.txt` 驗證 archive hash。
2. 使用 `release-assets.json` 驗證 canonical asset metadata。
3. 使用 `release-sbom.spdx.json` 驗證 release asset SBOM sidecar。它是 asset-level release archive inventory，not a full package/dependency SBOM。
4. 使用 signer pin 驗證 signed payload。
5. 使用 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 作為必要且獨立的 exact certificate thumbprint trust root；相鄰 sidecar 只能證明 archive provenance，不能取代 signer trust。
6. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 certificate subject additional constraint。

Agent 應回報 signer pin policy 與驗證結果，不應回報 certificate secrets。

## 確認後安裝命令

使用者確認 plan 與來源驗證後，只有在對應 GitHub Release assets 已存在時才使用公開 HTTPS alias；否則使用這類已審查的本機命令形狀：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 `
  -PackageArchivePath .\release\release_<version>_win-<arch>.zip `
  -TrustedReleaseMetadataDirectory .\release `
  -Architecture <arch> `
  -Client <client-id> `
  -InstallRoot "<confirmed-install-root>" `
  -NonInteractive -Force -OutputJson
```

請用核准 plan 中的具體值替換 placeholder。只有在使用者同意覆寫或更新既有 registration artifact 時，才保留 `-Force`。

## Client registration

取得確認後，根據通過驗證的 release acquisition path，呼叫已審查的公開 HTTPS alias、本機 `scripts/online-installer.ps1`，或 package-local `run.bat`。`<InstallRoot>\<arch>\client-registration\` 底下產生的 registration artifact 是唯一真源。

檢查選定 client 的 artifact：

- `claude-code` 與 `codex` 使用 CLI registration。
- `cursor`、`vscode`、`visual-studio` 與 `claude-desktop` 使用 JSON registration。
- `other` 使用 artifact-only guidance。

## Code signing 邊界

Normal install path：

- 驗證已發布 release signature 與 signer pin policy。
- 不處理 private key、PFX password、GitHub secrets 或 certificate export material。

Release signing helper path：

- Agent 可以解釋 Authenticode signing 概念與本機命令。
- 使用者必須讓 certificate 與 secrets 留在本機。
- self-signed certificate 只適用 local/dev/test，不具 production trust。
- 本機 artifact 簽章後，重新產生 `SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json`。

## External E2E validation checklist

- 從 fresh clone from GitHub 開始，不要使用呼叫者的 local worktree。
- 安裝前先閱讀這份 guide 與 `AGENT_INSTALL.md`。
- 使用 `-Version latest -Prerelease` 搭配明確 `-InstallRoot` 與 `-WorkingRoot` 安裝最新 GitHub pre-release package。
- 從 clone 啟動 golden WPF TestApp。
- 透過 STDIO 啟動安裝後的 packaged MCP server，並送出 real JSON-RPC requests。
- 驗證 `tools/list` 回報 64 tools。
- 驗證 `connect`、`get_active_process`、`get_ui_summary` 與一個 safe read tool。
- 將 P0/P1 blockers 與 P2/P3 documentation 或 polish findings 分開回報。

## 疑難排解

- 如果 OS 不是 Windows，停止並回報 server 僅支援 Windows。
- 如果 architecture 不確定，安裝前請使用者確認。
- 如果缺少 sidecar，不要安裝。
- 如果 signer pin 驗證失敗，停止。
- 如果 CLI discovery 被 elevation 擋住，改用 generated artifact 並要求使用者手動註冊。
- 如果安裝後 `connect()` 失敗，確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`、process architecture，以及該 target 是否允許 raw injection。

## 可複製 Agent prompt

```text
Read AGENT_INSTALL.md or docfx/guides/agent-assisted-install.md. Do not install yet. Run powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Action plan -OutputJson for read-only discovery, then present a plan that includes version, releaseChannel, architecture, install root, client id, release archive, SHA256SUMS.txt, release-assets.json, release-sbom.spdx.json, release-evidence.json, and signer pin policy. Ask for confirmation before mutation. After approval, use irm https://installer.wpf-mcptools.evanlau1798.com | iex only when the matching GitHub Release assets exist; otherwise use the GitHub pre-release online-installer E2E path or a reviewed local package fallback. Inspect generated client-registration artifacts, verify the installed executable, and report results without secrets.
```
