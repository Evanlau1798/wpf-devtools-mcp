# Agent 輔助安裝

此頁供 AI agent 協助使用者安裝 WPF DevTools MCP。它不是 release runbook；agent 只能在使用者明確確認後才可下載、安裝或寫入設定。

## 職責

- 偵測 platform、architecture、可用 MCP clients 與可重用 install root。
- 在任何 mutation 前提出具體 plan。
- 驗證 release archive sidecars 與 signer-pin policy。
- 以 generated `client-registration` artifact 作為 registration 真源。
- 不列印 secrets，也不要求 signing material。

## Read-only planning command

```powershell
pwsh -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson
```

如果 PowerShell 7 不可用，請使用 Windows PowerShell 執行相同的 read-only action：

```powershell
powershell.exe -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson
```

Plan command 會回報 supported clients、detected clients、architecture、default install root 與是否會 mutate files。請把輸出視為 evidence，而不是 permission。

Supported client ids 為 `claude-code`、`codex`、`cursor`、`vscode`、`visual-studio`、`claude-desktop` 與 `other`。

## 必要使用者確認

安裝前請要求使用者確認：

- version 或 release tag
- architecture：`x64`、`x86` 或 `arm64`
- install root
- client registration target
- 是否允許更新既有 registration artifact

## Release artifacts

手動 production review 前，請把這些檔案與 archive 放在同一層：`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json` 與 `package-sbom.spdx.json`。

`release-sbom.spdx.json` 描述 release asset/archive inventory。`package-sbom.spdx.json` 描述 package、相依性、script、assembly 與 payload contents。Payload 簽章驗證仍需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。

## 使用者核准後安裝

在第一個 stable GitHub Release 發布前，請使用 preview pre-release alias：

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

目前 public onboarding 使用 `-Prerelease`；第一個 stable GitHub Release 發布後，stable install 可以省略這個 switch。

已審查本機 package command：

```powershell
pwsh -NoProfile -File .\scripts\online-installer.ps1 `
  -PackageArchivePath .\release\release_<version>_win-<arch>.zip `
  -TrustedReleaseMetadataDirectory .\release `
  -Architecture <arch> `
  -Client <client-id> `
  -InstallRoot "<confirmed-install-root>" `
  -NonInteractive -Force -OutputJson
```

如果已審查 Windows host 沒有 PowerShell 7，請以 `powershell.exe -NoProfile -File` 搭配相同 arguments 執行。

Prerelease/debug trust boundary：如果 noninteractive install 需要在允許 `DebugTrustedRootSkip` 前證明 dev/Debug package，請使用上方已審查本機 package command，讓 online installer 先用 `-TrustedReleaseMetadataDirectory` 驗證 ZIP 後再解壓。不要把 `bin\install.ps1`、`bin/install.ps1` 或 `run.bat` 當作 noninteractive prerelease/debug trust path。這些 package-local entrypoints 可以在另外完成 sidecar verification 後使用，但它們本身無法證明 extracted files 來自哪個 archive。

Package-local fallback：

```powershell
.\run.bat
```

## 驗證與回報

安裝後回報：

- installed executable path
- generated registration artifact path
- selected client id
- release sidecar verification result
- checked signer pin
- 是否仍需要 manual registration step

不要回報 private keys、PFX passwords、GitHub secrets、auth secrets 或 certificate private-key material。

## 疑難排解

- 如果 host 不是 Windows，停止並回報 server 只支援 Windows/WPF target。
- 如果缺少 sidecar，不要安裝。
- 如果 signer verification 失敗，停止。
- 如果 client CLI discovery 被 elevation 擋住，改用 generated artifact 並請使用者手動註冊。
- 如果安裝後 `connect()` 失敗，確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`、process architecture 與 raw-injection policy。

## 可複製 prompt

```text
Read AGENT_INSTALL.md and docfx/guides/agent-assisted-install.md. Do not install yet. Run pwsh -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson for read-only discovery, or powershell.exe -NoProfile -File with the same arguments when PowerShell 7 is unavailable. Present a plan with version, architecture, install root, client id, release archive, sidecars, and signer pin policy. Ask for confirmation before mutation. After approval, use the preview pre-release alias, a reviewed local package command, or package-local run.bat. Report generated registration artifacts and do not print secrets.
```
