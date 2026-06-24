# 發行版配置

此頁說明公開 release assets、解壓後 package 與 installed copy 的穩定 folder contract。

## 正式生成來源

- Packaging source：`scripts/tools/packaging/Publish-Release.ps1`
- Sidecar source：`scripts/tools/packaging/Write-ReleaseSidecars.ps1`
- Upload source：`scripts/tools/packaging/Export-GitHubReleaseAssets.ps1`
- Online installer source：`scripts/online-installer.ps1`

下列說明描述這些 scripts 的輸出結果，而不是取代 scripts 本身。

## 公開 release assets

安裝最新 stable release：

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

通用 archive pattern 是 `release_<version>_win-<arch>.zip`。Stable release archive 命名為：

- `release_<version>_win-x64.zip`
- `release_<version>_win-x86.zip`

ARM64 發行檔可作為 preview asset 提供，但目前不保證穩定性，因為尚無可行的 Windows-on-ARM runtime 驗證硬體。Preview ARM64 archive 使用：

- `release_<version>_win-arm64.zip`

Production review 時，請讓 archive 與下列 sidecar 保持相鄰：

| 檔案 | 意義 |
| --- | --- |
| `SHA256SUMS.txt` | Archive checksum verification |
| `release-assets.json` | Canonical asset metadata 與 sidecar hashes |
| `release-sbom.spdx.json` | Release asset/archive inventory |
| `package-sbom.spdx.json` | Package、相依性、script、assembly 與 payload SBOM |

`release-sbom.spdx.json` 與 `package-sbom.spdx.json` 是刻意分開的 artifact。

Release package 有兩種 trust mode。`Signed` package 使用 Authenticode payload verification，並需要 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。付費簽章尚不可用時，beta prerelease package 可以使用 `ReleaseChecksumOnly`；installer 只有在 GitHub Release sidecars 或明確 trusted metadata directory 透過 SHA256 release metadata 證明 archive 後，才接受此模式。

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

## Online installer source contract

`scripts/online-installer.ps1` 是 public installer alias 與 generated single-file release artifact 的 canonical source entrypoint。release pipeline 必須一起驗證 source entrypoint、packaged artifact 與 public alias，確保它們解析相同 release assets，並套用相同 package verification behavior。

未來若重整 helper logic，必須維持此 contract，並讓 packaged helper manifest、generated artifact 與 public installer endpoint 保持同步。
