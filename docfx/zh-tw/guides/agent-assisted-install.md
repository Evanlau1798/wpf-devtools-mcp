# Agent 輔助安裝

當 AI agent 協助使用者安裝 WPF DevTools MCP 時，請使用這份規格。Agent 必須先規劃、在使用者確認前避免副作用、驗證 release provenance，並且只呼叫已審查的本機 installer entrypoint。

## Agent 契約

- 先讀這份 guide 或 `AGENT_INSTALL.md`。
- 尚未安裝。
- 先提出 plan 並取得明確同意；任何修改前必須取得使用者確認。
- Public endpoint smoke check 完成前，不要執行 moving-branch raw script 或遠端一行安裝命令。
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

Public endpoint smoke check 通過前，請優先使用本機產生的 package 或已下載的 release asset，不要提供遠端一行安裝命令。

## 來源驗證

解壓或安裝前：

1. 使用 `SHA256SUMS.txt` 驗證 archive hash。
2. 使用 `release-assets.json` 驗證 canonical asset metadata。
3. 使用 signer pin 驗證 signed payload。
4. 若已驗證的 sidecar 不再與 package 相鄰，使用 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 作為必要的 exact certificate thumbprint trust root。
5. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 certificate subject additional constraint。

Agent 應回報 signer pin policy 與驗證結果，不應回報 certificate secrets。

## 確認後安裝命令

使用者確認 plan 與來源驗證後，使用這類已審查的本機命令形狀：

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

取得確認後，才呼叫已審查的本機 `scripts/online-installer.ps1` 或 package-local `run.bat`。`<InstallRoot>\<arch>\client-registration\` 底下產生的 registration artifact 是唯一真源。

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
- 本機 artifact 簽章後，重新產生 `SHA256SUMS.txt` 與 `release-assets.json`。

## 疑難排解

- 如果 OS 不是 Windows，停止並回報 server 僅支援 Windows。
- 如果 architecture 不確定，安裝前請使用者確認。
- 如果缺少 sidecar，不要安裝。
- 如果 signer pin 驗證失敗，停止。
- 如果 CLI discovery 被 elevation 擋住，改用 generated artifact 並要求使用者手動註冊。
- 如果安裝後 `connect()` 失敗，確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`、process architecture，以及該 target 是否允許 raw injection。

## 可複製 Agent prompt

```text
Read AGENT_INSTALL.md or docfx/guides/agent-assisted-install.md. Do not install yet. Run powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Action plan -OutputJson for read-only discovery, then present a plan that includes version, architecture, install root, client id, release archive, SHA256SUMS.txt, release-assets.json, and signer pin policy. Ask for confirmation before mutation. After approval, run only the reviewed local installer or package-local run.bat, inspect generated client-registration artifacts, verify the installed executable, and report results without secrets.
```
