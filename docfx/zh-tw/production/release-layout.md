# 發行版配置

此頁說明公開 release assets、解壓後 package 與 installed copy 的穩定 folder contract。

## 正式生成來源

- Packaging source：`scripts/tools/packaging/Publish-Release.ps1`
- Sidecar source：`scripts/tools/packaging/Write-ReleaseSidecars.ps1`
- Upload source：`scripts/tools/packaging/Export-GitHubReleaseAssets.ps1`
- Online installer source：`scripts/online-installer.ps1`

下列說明描述這些 scripts 的輸出結果，而不是取代 scripts 本身。

## 公開 release assets

在第一個 stable GitHub Release 發布前，請使用 preview pre-release command：

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

只有在 stable release assets 與 anonymous endpoint smoke checks 通過後，才使用 default stable `latest` channel。

目前 release archive 命名為：

- `release_<version>_win-x64.zip`
- `release_<version>_win-x86.zip`
- `release_<version>_win-arm64.zip`

Production review 時，請讓 archive 與下列 sidecar 保持相鄰：

| 檔案 | 意義 |
| --- | --- |
| `SHA256SUMS.txt` | Archive checksum verification |
| `release-assets.json` | Canonical asset metadata 與 sidecar hashes |
| `release-sbom.spdx.json` | Release asset/archive inventory |
| `package-sbom.spdx.json` | Package、相依性、script、assembly 與 payload SBOM |
| `release-evidence.json` | Release evidence 與 audit bundle |

`release-sbom.spdx.json` 與 `package-sbom.spdx.json` 是刻意分開的 artifact。兩者都不能取代 signer trust；Release payload 簽章驗證仍需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。

## 解壓後 package 結構

```text
release_<version>_win-x64/
  run.bat
  bin/
    install.ps1
    manifest.json
    wpf-devtools-x64.exe
    WpfDevTools.Mcp.Server.dll
    WpfDevTools.Injector.dll
    WpfDevTools.Shared.dll
    inspectors/
      net8.0-windows/
        WpfDevTools.Inspector.dll
      net48/
        WpfDevTools.Inspector.dll
    bootstrapper/
      x64/
        WpfDevTools.Bootstrapper.x64.dll
    installer/
      installer-helpers.manifest.json
      Installer.Actions.ps1
      Installer.Uninstall.ps1
      Tui.Flow.ps1
      ...
```

## 安裝後 layout

```text
<InstallRoot>\<arch>\
  current/
    bin/
      manifest.json
      wpf-devtools-<arch>.exe
      WpfDevTools.Mcp.Server.dll
      WpfDevTools.Injector.dll
      WpfDevTools.Shared.dll
      inspectors/
      bootstrapper/
      installer/
  client-registration/
    claude-code.txt
    codex.txt
    claude-desktop.json
    cursor.global.json
    cursor.project.json
    vscode.json
    visual-studio.json
    other.mcpServers.json
  install-manifest.json
```

## Contract notes

- MCP client 應註冊 `bin/wpf-devtools-<arch>.exe`。
- `bin/inspectors` 與 `bin/bootstrapper` 必須與 installed server content 保持相鄰。
- `bin/installer` 是 integrity-checked helper bundle，用於 package 與 recovery flows。
- `run.bat` 是 package-root entrypoint，適合不想直接呼叫 PowerShell 的使用者。
- `client-registration` 會在安裝時產生，是 MCP client setup 的公開 copy-paste 真源。
- 若省略 `-InstallRoot`，installer 會優先重用最後一個 live install root；只有沒有可重用 root 時，才 fallback 到 `%APPDATA%\WpfDevToolsMcp`。

## Online installer source-size 暫時例外

`scripts/online-installer.ps1` 目前刻意保持為 thin source entrypoint，且可被 package 成 generated single-file release artifact。這是 normal source file size target 的暫時例外：在目前的 production remediation loop 期間，不要拆分它，除非 generated single-file release artifact 與 public installer alias 已一起完成驗證。

Post-remediation，請重新檢視此例外；只有 release pipeline 能證明 source entrypoint、generated single-file release artifact 與 installer alias 仍產生相同 package verification behavior 時，才拆分 helper logic。
