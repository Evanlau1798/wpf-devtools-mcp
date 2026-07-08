# Agent 輔助安裝

此頁供 AI agent 協助使用者安裝 WPF DevTools MCP。它不是 release runbook；agent 只能在使用者明確確認後才可下載、安裝或寫入設定。

## 職責

- 偵測 platform、architecture、可用 MCP clients 與可重用 install root。
- 在任何 mutation 前提出具體 plan。
- 驗證 release archive sidecars 與 release trust policy。
- 以 generated `client-registration` artifact 作為 registration 真源。
- 不列印 secrets，也不要求 signing material。

## Read-only planning command

一般使用者 setup 請透過 public installer entrypoint 執行 read-only plan：

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Action plan -OutputJson
```

若你正在審查已 checkout 的 source tree，而不是協助一般使用者，才使用本機 script path：

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

手動 package review 前，請把 `release_<version>_win-<arch>.zip`、`SHA256SUMS.txt`、`release-assets.json`、`release-sbom.spdx.json` 與 `package-sbom.spdx.json` 放在一起。`Signed` package 需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`；`ReleaseChecksumOnly` beta package 需要 GitHub Release sidecars 或 trusted metadata directory 提供 SHA256 release metadata。使用者流程請見 [手動驗證安裝](../quickstart/manual-install.md)，完整 artifact contract 請見 [發行版配置](../production/release-layout.md)。

## 使用者核准後安裝

使用者明確核准後，預設 online installer path 為：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

預設 online installer path 不需要 agent 先下載 release archive 或 sidecar。Installer 會解析所選 GitHub Release package、驗證 release metadata、安裝 packaged executable，並寫出 generated `client-registration` artifacts。

Pinned public pre-release：

```powershell
$version = 'v1.0.0-beta.23'
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version $version -Prerelease
```

ARM64 發行檔可作為 preview asset 提供，但目前不保證穩定性，因為尚無可行的 Windows-on-ARM runtime 驗證硬體。

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

當 installer 必須在解壓前證明本機 archive 時，請使用上方已審查本機 package command。不要把 `bin\install.ps1`、`bin/install.ps1` 或 `run.bat` 當作 noninteractive prerelease/debug trust path。`DebugTrustedRootSkip` 是 development package policy，不是繞過 archive verification 的捷徑。

Package-local fallback：

```powershell
.\run.bat
```

## 驗證與回報

安裝後回報：

- installed executable path
- generated registration artifact path
- selected client id
- selected installer path：預設 online installer path 或已審查本機 package
- 使用本機 sidecar 時的 release sidecar verification result
- release trust mode checked
- 是否仍需要 manual registration step

不要回報 private keys、PFX passwords、GitHub secrets、auth secrets 或 certificate private-key material。

## 疑難排解

- 如果 host 不是 Windows，停止並回報 server 只支援 Windows/WPF target。
- 如果缺少 sidecar，不要安裝。
- 如果 `Signed` package signer verification 失敗，停止。
- 如果 `ReleaseChecksumOnly` prerelease 的 SHA256 release metadata verification 失敗，停止。
- 如果 client CLI discovery 被 elevation 擋住，改用 generated artifact 並請使用者手動註冊。
- 如果安裝後 `connect()` 失敗，確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`、process architecture 與 raw-injection policy。

## 可複製 prompt

```text
Read AGENT_INSTALL.md and docfx/guides/agent-assisted-install.md. Do not install yet. Run the public installer with -Action plan -OutputJson for read-only discovery. Use the default online installer path unless the user explicitly asks for a reviewed local archive. Ask for confirmation before mutation. After approval, use the stable installer alias or pinned pre-release alias; use the reviewed local package command only for local archives. Use package-local run.bat only after sidecar verification. Report generated registration artifacts and do not print secrets.
```
