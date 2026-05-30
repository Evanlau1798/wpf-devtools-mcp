# 部署指南

## 部署模型

這個 server 通常會以本機 Windows companion process 的形式部署，並與目標 WPF 應用程式並存。

若由 AI agent 協助安裝，請依照 [Agent 輔助安裝](../guides/agent-assisted-install.md)，讓 discovery、confirmation、provenance verification 與 client registration 維持分離。

## 正式腳本來源

installer 與 packaging 行為定義在 `scripts/`，而不是文件站台本身：

- `scripts/online-installer.ps1`
- `scripts/tools/packaging/Publish-Release.ps1`
- `scripts/installer/Installer.Actions.ps1`

## 建議安裝模式

### 已審查的本機 package 安裝

> **公開端點狀態：** Public release endpoints are not yet anonymously reachable。GitHub repository、Releases、latest-release API、raw installer URL 與 installer alias 都通過匿名 smoke check 前，請使用本機產生且已驗證的 release package 或 source checkout，不要執行遠端一行安裝命令。

請先審查 `scripts/online-installer.ps1` 作為維護者來源。這支已審查的 installer 可安裝本機 package archive、在解壓前驗證 archive integrity，然後透過已審查的 installer/helper flow 安裝解壓出的 packaged payload。

本機 package 範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

預設互動流程會詢問 release 版本，以目前電腦架構（`x64`、`x86` 或 `arm64`）作為預設架構，接著詢問要產生哪一種 MCP client registration。省略 `-Architecture` 時，installer 會偵測系統架構；只有在刻意安裝不同套件時才傳入 `-Architecture`。

指定 client 的自動化範例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

### 公開 release package 備援路徑

1. 使用本機產生的 package，或等 public endpoint smoke check 通過後，再從 [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) 下載符合架構的 `release_<version>_win-<arch>.zip`、`SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json`。
2. 解壓前，先用 `SHA256SUMS.txt` 與 `release-assets.json` 驗證 archive。保留 `release-sbom.spdx.json` 作為 published package SBOM。
3. 解壓縮套件。
4. 執行 `run.bat`。

在信任解壓後的套件前，請保留與該 archive 相鄰且已驗證的 release sidecar：`SHA256SUMS.txt` 用於 checksum 驗證，`release-assets.json` 用於 canonical release metadata，`release-sbom.spdx.json` 用於 package SBOM。如果解壓後的套件旁已沒有原始且已驗證的 archive 與 sidecar，請在執行 `run.bat` 前設定 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 作為必要的 thumbprint trust root；`WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` 只能在 thumbprint 已 pin 之後作為 additional constraint。

`run.bat` 會在目前 shell 尚未提升權限時要求 elevation，然後啟動 packaged `bin/install.ps1`。如果你需要把安裝留在目前未提升權限的 shell 中，請設定 `WPFDEVTOOLS_SKIP_ELEVATION=1`。

## 遠端腳本執行是可選的

任何 package-local bootstrap 流程都只是備援方案。請先審查 repo 內的原始碼，並把 `scripts/` 視為唯一的權威實作。

## Release layout 很重要

bootstrapper 與 inspector sidecar 會相對於 server 位置被解析，因此文件中的 release layout 必須在安裝與升級後維持穩定。

完整契約請參考 [Release Layout](release-layout.md)。

## Server Native AOT 與 trimming 邊界

目前 packaged server 是 framework-dependent 的 non-AOT Windows process。它不是 Native AOT、trimmed 或 single-file server distribution。

這是目前 release line 的刻意選擇，因為 `src/WpfDevTools.Mcp.Server/Program.cs` 透過 assembly-based discovery 註冊 MCP tools、prompts 與 resources：

- `WithToolsFromAssembly`
- `WithPromptsFromAssembly`
- `WithResourcesFromAssembly`

MCP C# SDK 會用 `RequiresUnreferencedCode` 標記這些 non-generic assembly discovery APIs，因為它們會動態查找 method metadata，在 Native AOT 或 aggressive trimming deployment 中可能無法正常運作。若未來要支援 Native AOT、trimming 或 single-file server distribution，必須先改成 AOT-safe 的 explicit/generic registrations，再新增 publish-time tests，證明 `tools/list`、prompts、resources 與 response schemas 在 trimming 後仍然存在。

## 已安裝 executable 契約

MCP client 應直接啟動安裝後解析出的 `wpf-devtools-<arch>.exe`，例如：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

如果你沒有傳入 `-InstallRoot`，installer 會先嘗試重用最後一個仍有 live evidence 的 install root；只有找不到可重用路徑時，才會回退到 `%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe`。

## 已簽章 payload provenance 檢查清單

- 在取得安裝驗證證據前，保留與下載 archive 相鄰且已驗證的 `SHA256SUMS.txt`、`release-assets.json` 與 `release-sbom.spdx.json`。
- 依照 pinned release signer 驗證已簽章 release payload，包括 `bin/wpf-devtools-<arch>.exe`、`bin/inspectors` 與 `bin/bootstrapper`。如果已驗證的 sidecar 不再與解壓後套件相鄰，請設定 `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`。
- 從解壓後的 package 先執行 `run.bat` 進行 package-local smoke，接著驗證 `get_processes`、`connect` 與 `get_ui_summary`，再將該安裝註冊到使用者工具。
- 從最終已安裝路徑啟動 `<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe`，重複執行 smoke 驗證。

## 生產環境檢查清單

- 使用與 target process 相同的架構。
- 保持 `inspectors` 與 `bootstrapper` 目錄與安裝後的 server 內容相鄰。
- 對 release inspector binaries 進行簽章。
- 在硬化環境中設定 authentication 與 TLS。
- 在 repository 外，從已安裝路徑實際驗證 `get_processes`、`connect`，以及一個 scene-level 呼叫，例如 `get_ui_summary`。
