# Release Layout

此頁說明公開 release asset、解壓後 package，以及安裝後目錄的穩定結構契約。

## 正式生成來源

- Packaging source: `scripts/tools/packaging/Publish-Release.ps1`
- Online installer source: `scripts/online-installer.ps1`

下列說明描述的是這些腳本的輸出結果，而不是取代這些腳本本身。

## 公開 release asset

> **公開端點狀態：** Public release endpoints are not yet anonymously reachable。GitHub repository、Releases、latest-release API、raw installer URL 與 installer alias 都通過匿名 smoke check 前，請先把下列 release 名稱視為本機產生 artifact 的 package layout 契約。

目前正式的 release 壓縮檔命名為：

- `release_<version>_win-x64.zip`
- `release_<version>_win-x86.zip`
- `release_<version>_win-arm64.zip`

等 public endpoint smoke check 通過後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載。

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

## 安裝後目錄結構

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
        installer-helpers.manifest.json
        Installer.Actions.ps1
        Installer.Uninstall.ps1
        Tui.Flow.ps1
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

## 契約說明

- MCP client 應註冊 `bin/wpf-devtools-<arch>.exe`。
- `bin/inspectors` 與 `bin/bootstrapper` 是 sidecar 目錄，必須與安裝後的 server 內容保持相對位置。
- `bin/installer` 是經過完整性驗證的 helper bundle，供 packaged installer 與 standalone recovery flow 使用，必須與封裝或安裝後的 server 內容保持相對位置。
- `run.bat` 是 package root 的使用者入口，適合不想直接執行 PowerShell 的使用者。
- `bin/install.ps1` 是 canonical TUI-first installer 腳本在 package 內的複本，並保留 CLI fallback。
- `client-registration` 會在安裝時產生，並作為 AI client setup 的公開 copy-paste 來源。
- 若省略 `-InstallRoot`，installer 會優先重用最後一個仍可存取的 live install root；只有沒有可重用途徑時，才會使用 `%APPDATA%\WpfDevToolsMcp` 作為回退根目錄。

## Online installer source exception

`scripts/online-installer.ps1` 目前仍作為 canonical single-file release artifact source 維護，讓公開安裝說明、package 內的 `bin/install.ps1` 與 recovery flow 保持 byte-for-byte 對齊。
這是對一般 source file size target 的明確暫時例外；不要在目前的 production remediation loop 內拆分它。

Post-remediation，應排程一次 packaging refactor：保留 thin source entrypoint，並為正式發布組合 generated single-file release artifact。
該後續工作必須維持公開 `scripts/online-installer.ps1` contract，同時把可重用實作移到較小的 helper modules，並由既有 release packaging smoke tests 覆蓋。
