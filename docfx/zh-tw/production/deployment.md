# 部署指南

此頁描述已審查 WPF DevTools MCP release package 的 production deployment path。Maintainer release qualification 與 sandbox automation 應放在 `RELEASING.md`。

## 部署輸入

Production review 時，請把下列檔案放在一起：

| 檔案 | Production meaning | Requirement |
| --- | --- | --- |
| `release_<version>_win-<arch>.zip` | 版本化 release package | 必要 |
| `SHA256SUMS.txt` | Archive checksum verification | 必要 |
| `release-assets.json` | Canonical release asset metadata | 必要 |
| `release-sbom.spdx.json` | Release asset/archive inventory | Release governance 必要 |
| `package-sbom.spdx.json` | Package、相依性、script、assembly 與 payload SBOM | 完整 production review 必要 |

`release-sbom.spdx.json` 與 `package-sbom.spdx.json` 是不同 artifact。Sidecar 可證明 provenance 與 review scope。`Signed` package 仍需要使用 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 做 payload 簽章驗證；beta prerelease package 只有在 archive 已透過 SHA256 release metadata 驗證時，才可以使用 `ReleaseChecksumOnly`。

## 安裝路徑

安裝最新 stable release：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

ARM64 發行檔可作為 preview asset 提供，但目前不保證穩定性，因為尚無可行的 Windows-on-ARM runtime 驗證硬體。

已審查本機 package command：

```powershell
pwsh -NoProfile -File .\scripts\online-installer.ps1 `
  -PackageArchivePath .\release\release_<version>_win-<arch>.zip `
  -TrustedReleaseMetadataDirectory .\release `
  -Client other `
  -NonInteractive -Force -OutputJson
```

Sidecar 驗證後的 package-local fallback：

```powershell
.\run.bat
```

需要 artifact-only registration output 時使用 `-Client other`。只有在使用者核准寫入該 client configuration 時，才使用具體 client id。

## Trust 與 signer policy

1. 使用 `SHA256SUMS.txt` 驗證 archive hash。
2. 使用 `release-assets.json` 驗證 asset entry 與 sidecar hash。
3. Review `release-sbom.spdx.json` 的 release assets。
4. Review `package-sbom.spdx.json` 的 package contents 與 dependencies。
5. 對 `Signed` package，使用 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` pin 預期 Authenticode signer。
6. 對 `ReleaseChecksumOnly` beta prerelease，確認 GitHub Release notes 與 `release-assets.json` 針對選定 archive 發布 SHA256 release metadata。
7. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 後作為額外 subject constraint。

## 已簽章 payload provenance 檢查清單

Production 使用前，請先確認：

1. 用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證下載的 `release_<version>_win-<arch>.zip`。
2. 確認 `release-sbom.spdx.json` 是 release asset SBOM，不是完整 package/dependency SBOM。
3. 確認 `package-sbom.spdx.json` 涵蓋 package、dependency、script、assembly 與 payload contents。
4. 在 payload 簽章驗證前 pin `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。
5. 大規模安裝前，先從解壓後 archive 執行 package-local 啟動驗證。
6. 從最終已安裝路徑確認 `wpf-devtools-<arch>.exe` 位於已審查 install root。

## Checksum-only prerelease 檢查清單

信任未付費簽章的 beta prerelease package 前，請先確認：

1. GitHub Release 已標記為 prerelease。
2. Package manifest 使用 `ReleaseChecksumOnly`。
3. 使用 `SHA256SUMS.txt` 與 `release-assets.json` 中的 SHA256 release metadata 驗證 archive。
4. 解壓前 review 兩份 SBOM。
5. 執行 package-local 啟動驗證，並確認最終 installed path。

## Install root 與 registration

若省略 `-InstallRoot`，installer 會優先重用最後一個 live install root，否則 fallback 到 `%APPDATA%\WpfDevToolsMcp`。安裝後 layout：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
<InstallRoot>\<arch>\client-registration\
```

請從 generated `client-registration` artifacts 註冊 MCP client，不要手寫 path。

## Runtime policy

只設定 deployment profile 必要的 gates：

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS=<exact target exe>` 是必要設定。
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` 允許 sensitive UI 與 runtime reads。
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` 允許 screenshot capture。
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` 允許 ViewModel tools 與 conditional ViewModel captures。
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` 允許已核准 mutation/interaction workflows。
- `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS=<exact target exe>` 是 raw injection fallback 的必要設定。

如果你擁有 target app，優先使用 SDK-hosted Inspector reuse。SDK reuse 要求兩個 process 使用相同 `WPFDEVTOOLS_AUTH_SECRET` 與同一個 local absolute `WPFDEVTOOLS_CERT_DIR`。

## Server distribution boundary

Packaged MCP server 是 non-AOT .NET distribution。它透過 `WithToolsFromAssembly`、`WithPromptsFromAssembly` 與 `WithResourcesFromAssembly` 做 assembly discovery；目前 tool/resource model 不是 Native AOT contract。除非專用 release lane 已證明 package 仍保留所有 reflected tool、prompt、resource 與 `RequiresUnreferencedCode` boundary，否則請把 server package trimming 視為不支援。

Target application packaging 是另一個邊界。Native AOT WPF target 不支援。Trimmed target 可能讓 raw injection 或 inspector startup 不穩定；若 target app owner 能驗證 startup，請優先使用 SDK-hosted reuse。

## Dependency audit cadence

Release promotion 前，針對 locked solution 執行 `dotnet restore --locked-mode` 與 `dotnet list package --vulnerable`。Review NuGet audit output 的 direct 與 transitive dependencies，尤其是 `ModelContextProtocol` 與 `System.Text.Json`。GitHub Actions dependency alerts 與 verified advisories 可作為 triage evidence；沒有 actionable advisory 或 affected-version match 時，不要發布 speculative CVE claims。

## Rollback 與 uninstall

- 使用前一個已審查 package 重新執行 installer 以 rollback。
- 使用 `-Action uninstall` 搭配 `-Client <client-id>` 只移除或驗證 selected registration。若使用 `-Client other`，selected registration target 是產生的 `other.mcpServers.json` artifact；installer-owned server files 會保留，供其他 clients 或之後重用。
- 使用 `-Action full-uninstall -InstallRoot <exact-root>` 時，只會移除該 root 下的 registrations、generated client-registration artifacts 與 installer-owned server payloads。只有測試執行後或 decommissioning workflow 需要移除所有 detected installer roots 時，才省略 `-InstallRoot`。
- 只有 deployment policy 要求 rotation 或 decommissioning 時，才手動移除 persisted auth secrets 與 certificates。

## Operational verification

從最終 installed path 驗證：

1. 啟動 target WPF application。
2. 啟動 configured MCP client。
3. 呼叫 `connect`。
4. 呼叫 `get_active_process` 與 `get_ui_summary`。
5. 在啟用高風險能力前，確認未啟用的 gates 會 fail closed。
