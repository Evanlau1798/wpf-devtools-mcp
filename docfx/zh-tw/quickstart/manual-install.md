# 手動驗證安裝

只有在你已經取得已審查的 release archive，且需要從本機檔案安裝時，才使用此路徑。一般 onboarding 請優先使用 [5 分鐘快速開始](index.md) 的 public installer。

## 需要下載的檔案

請讓 archive 與 sidecar 位於同一個目錄：

| 檔案 | 用途 |
| --- | --- |
| `release_<version>_win-<arch>.zip` | 要安裝的 package |
| `SHA256SUMS.txt` | Archive hash verification |
| `release-assets.json` | Release asset metadata 與 sidecar hashes |
| `release-sbom.spdx.json` | Release asset/archive inventory |
| `package-sbom.spdx.json` | Package、dependency、script、assembly 與 payload inventory |

ARM64 發行檔可作為 preview asset 提供，但目前不保證穩定性，因為尚無可行的 Windows-on-ARM runtime 驗證硬體。

## 安裝前驗證

1. 使用 `SHA256SUMS.txt` 驗證 ZIP hash。
2. 確認 `release-assets.json` 中選定 asset 與 sidecar hash。
3. Review 兩份 SBOM，確認 release assets 與 package payloads。
4. 檢查 release trust mode：
   - `Signed` 需要使用 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 做 signer-pin verification。
   - `ReleaseChecksumOnly` 只允許 beta prerelease，且 GitHub Release metadata 必須能驗證 archive SHA256。

`ReleaseChecksumOnly` 只有在 exact payload bytes 仍可與 original reviewed archive 比對時，才能保護 raw injection。installed manifest 本身不是 trust root。Unsigned raw injection 必須把 original archive 與 `SHA256SUMS.txt` 保留在 installed payload 之外，並在 MCP client process 設定 `WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY`；否則請使用 `Signed` installed payloads。

如果缺少任何 sidecar，或 archive hash 不相符，請停止。

## 從已審查 archive 安裝

使用 public installer entrypoint，並帶入原始 archive 與 sidecar directory：

```powershell
$version = '1.0.0-beta.90'
$arch = 'x64'
$archive = (Resolve-Path ".\release_${version}_win-$arch.zip").Path
$metadata = Split-Path -Parent $archive
$installRoot = Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'

& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) `
  -Action install `
  -Architecture $arch `
  -Client other `
  -InstallRoot $installRoot `
  -PackageArchivePath $archive `
  -TrustedReleaseMetadataDirectory $metadata
```

安裝後 server path 應解析到：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

請從 generated `client-registration` 目錄註冊 MCP client，不要手寫路徑。

## Portable package 檢查

只有在完成 sidecar verification 後，且符合下列任一條件時，才使用 package-local `run.bat`：

- 解壓後 package 仍位於原始 archive 與 `SHA256SUMS.txt` 旁邊
- `WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY` 指向包含原始 archive 與 checksum sidecar 的目錄

```powershell
.\run.bat
```

一般 MCP client setup 仍建議使用 installed executable path，而不是 package-local executable。

## 後續 connect 失敗時

如果 `connect()` 回傳 `SecurityError: Security verification failed`，請先確認 MCP client 指向 `<InstallRoot>\<arch>\current\bin\` 下的 installed executable。若你刻意使用 portable path，請確認 package 仍能依上方說明找到原始 archive metadata。
