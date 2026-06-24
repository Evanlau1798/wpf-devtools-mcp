# 疑難排解

## `connect` 立即失敗

請先檢查：

- 目標 process 是正在執行的 WPF 應用程式
- raw injection/bootstrapper fallback 使用與 target process 相同架構的 server package
- SDK-hosted reuse 的 target-side Inspector host 已啟動，且 transport 設定相符

## 架構不相符

Raw injection/bootstrapper fallback 必須符合架構。最常見的 raw-injection 修正方式，是切換成與目標 process bitness 一致的安裝包。

- x64 target -> x64 package
- x86 target -> x86 package
- arm64 target -> arm64 package

SDK-hosted reuse 透過 named pipes 通訊；target-side host 已啟動後，不要求 server process 與 target process bitness 相同。只有在 server 必須自行 injection 時，才需要確認 server、bootstrapper 與 inspector sidecar 都來自同一組相符 bitness 的 package/build。

## 缺少執行時

如果 server 啟動後立刻結束，請確認對應發行包所需的 .NET runtime 已安裝。

## bootstrapper 解析

如果 `connect` 在找到 process 後仍失敗，請確認安裝資料夾中 `bootstrapper` 與 `inspectors` sidecar 仍與解析後的 `wpf-devtools-<arch>.exe` 位於預期位置。

## release trust verification failure

如果手動 package setup 後 `connect()` 回傳 `SecurityError: Security verification failed`，請先檢查 server path。MCP client 應指向 installed executable：

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

不要註冊解壓 archive 內的 package-local executable。對 checksum-only prerelease，請讓 archive 與 trusted sidecars 位於同一目錄，並用 packaged installer 搭配 `-PackageArchivePath` 與 `-TrustedReleaseMetadataDirectory` 執行，讓 runtime trust 在 raw injection 前完成解析。

## elevated target 與系統管理員權限不一致

如果目標 WPF app 是以系統管理員權限或其他 elevated 狀態啟動，而 MCP host 不是，通常仍可看到 process，但無法真正控制它。這類情況常會在 `connect`、injection 或後續工具呼叫時出現 `Access denied` 或中文系統上的「存取遭拒」。

建議處理方式：

- 以系統管理員權限重新啟動 Claude Code、Codex 或 MCP host，讓 server 與 target 位於相同完整性等級
- 改用非 elevated 的 target process 重新驗證
- 如果目標 packaging 或部署型態不適合一般 injection 路徑，請先在 target app 內呼叫 `InspectorSdk.Initialize()` 再呼叫 `connect()`；若啟用 TLS，也要使用相同的 transport config 與 local absolute `WPFDEVTOOLS_CERT_DIR`。Network paths are not allowed
- 如果 `connect()` 回傳 `CompatibilityError`，請重新啟動 target process，讓 MCP server 能重新 inject 或重用與目前 repo revision / compatibility contract 相符的 Inspector host
- 若既有的 SDK host 仍是 legacy plaintext，或本身沒有正常回應，MCP server 仍可能先得到 Timeout，而不是先證明 transport mismatch

## pipe readiness timeout

如果 injection 已開始，但 named pipe 始終沒有 ready，通常表示 target app 內部的啟動或 UI thread readiness 有問題。

## project-scoped registration confusion

如果 Claude Code 常常重新找不到 server，建議優先使用安裝時產生的 `client-registration/claude-code.txt`，或改用 project-scoped 註冊命令。project-scoped 設定可降低 shell、repo 與本機 profile 狀態不一致造成的漂移。

## 不支援的封裝型態與 injection 限制

若目標使用 self-contained single-file packaging，標準 injection 路徑無法使用。這時請優先考慮受支援的桌面封裝方式；若要用 SDK mode，請先在 target app 內呼叫 `InspectorSdk.Initialize()` 再呼叫 `connect()`。`connect()` 一定會先嘗試重用既有 SDK host，而 sidecar-free 的 executable layout 會得到最長的 reuse wait。

Trimmed deployment 仍有風險，因為 publish trimming 可能移除必要的 WPF 或 Inspector 型別；SDK-host reuse 是 preferred fallback，不是保證。Native AOT target 不支援，SDK-hosted reuse 不是 Native AOT workaround。
