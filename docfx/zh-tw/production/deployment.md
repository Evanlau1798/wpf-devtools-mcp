# 部署指南

## 部署模型

這個 server 通常會以本機 Windows companion process 的形式部署，並與目標 WPF 應用程式並存。

## 正式腳本來源

installer 與 packaging 行為定義在 `scripts/`，而不是文件站台本身：

- [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1)
- [scripts/tools/packaging/Publish-Release.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/tools/packaging/Publish-Release.ps1)
- [scripts/installer/Installer.Actions.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/installer/Installer.Actions.ps1)

## 建議安裝模式

### 已審查的腳本驅動安裝

請先審查 [scripts/online-installer.ps1](https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/scripts/online-installer.ps1) 作為維護者來源。這支已審查的 installer 會解析對應的 GitHub Release asset、在解壓前驗證 archive integrity，然後執行該 release 內版本相符的 `bin/install.ps1`。

建議範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64
```

指定 client 的自動化範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Version latest -Architecture x64 -Client claude-code -NonInteractive -Force -OutputJson
```

### 公開 release package 備援路徑

1. 從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載符合架構的 `release_<version>_win-<arch>.zip`。
2. 解壓縮套件。
3. 執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata。如果解壓後的套件旁已沒有原始且已驗證的 archive 與 sidecar，請在執行 `run.bat` 前設定 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`（或 `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`），讓本機安裝流程仍會強制要求明確的 signer pin。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

## 遠端腳本執行是可選的

任何 package-local bootstrap 流程都只是備援方案。請先審查 repo 內的原始碼，並把 `scripts/` 視為唯一的權威實作。

## Release layout 很重要

bootstrapper 與 inspector sidecar 會相對於 server 位置被解析，因此文件中的 release layout 必須在安裝與升級後維持穩定。

完整契約請參考 [Release Layout](release-layout.md)。

## 已安裝 executable 契約

MCP client 應直接啟動安裝後解析出的 `wpf-devtools-<arch>.exe`，例如：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

如果你沒有傳入 `-InstallRoot`，installer 會先嘗試重用最後一個仍有 live evidence 的 install root；只有找不到可重用路徑時，才會回退到 `%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe`。

## 生產環境檢查清單

- 使用與 target process 相同的架構。
- 保持 `inspectors` 與 `bootstrapper` 目錄與安裝後的 server 內容相鄰。
- 對 release inspector binaries 進行簽章。
- 在硬化環境中設定 authentication 與 TLS。
- 在 repository 外，從已安裝路徑實際驗證 `get_processes`、`connect`，以及一個 scene-level 呼叫，例如 `get_ui_summary`。
