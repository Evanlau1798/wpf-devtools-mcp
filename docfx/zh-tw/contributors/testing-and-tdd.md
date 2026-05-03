# 測試與 TDD

## 倉庫的基本要求

這個倉庫要求所有程式碼變更都遵循嚴格的 red-green-refactor 週期。

## 核心命令

為避免鎖檔問題，請把 build 與 test 分開執行：

```powershell
dotnet build WpfDevTools.sln -c Debug
dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --no-build
dotnet test tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj --no-build
```

## 目前驗證快照

目前專案層級的驗證狀態會結合最近一次完整完成的 suite baseline，以及測試數量或排程調整後的 focused rerun。

### 測試結果

- Unit tests：最近一次完整 unit-suite run 總數 3135，可由 `dotnet test --no-build --list-tests` 發現
- Integration tests：最近一次完整 integration-suite baseline 總數 301，可由 `dotnet test --no-build --list-tests` 發現
- 合計基準：3436 個測試，可由最近一次 unit 與 integration suite count refresh 發現

### Coverage

- 上一次合併 coverage 快照：line 83.4%、branch 71.8%、method 94.2%
- Coverage 來源：以 `coverlet.runsettings` 產生的 unit 與 integration Cobertura 報告合併而成；最近一次 full-suite 驗證未重新產生 coverage
- Coverage 報告仍包含可測的 `WpfDevTools.Injector` discovery 與 helper 程式碼
- 需要真實注入流程的 entry points 則透過 `[ExcludeFromCodeCoverage]` 排除

### 目前仍為紅色的切面

- 最近一次 unit 與 integration full-suite 驗證沒有剩餘紅色切面。
- 先前的 installer integrity、named-pipe compatibility、ping/replay、structured fallback、FileLogger shutdown，以及 `wait_for_dp_change_after_mutation` 切面目前皆由通過測試覆蓋。

## 涉及 MCP workflow 的變更

當工具語意或 server 行為改變時，建議用以下順序驗證：

1. unit tests
2. integration tests
3. 針對測試應用程式執行 live MCP smoke harness

## 涉及測試平行化的變更

Unit 與 integration suites 會啟用 collection-level parallelization，並用 CPU 數量調整 worker count。序列化的 collection lanes 應保持窄而明確，依照它們保護的 shared state 命名：

- installer PowerShell、TUI、process-lifecycle，以及 package-root 測試若需要彼此序列化，但仍應和不相關 collections 同時執行，請使用 `InstallerScripts`
- `TimingSensitive` 只用於在無關 workstation contention 下容易不穩的 timing-budget 測試
- `LiveBootstrapIntegration` collection 必須維持優先執行，因為 live DLL injection/connect smoke tests 在 shared testhost 累積長時間 WPF 與 MCP fixture 狀態前最穩定
- 除非某個 collection 不能和任何其他 collection 同時執行，否則避免設定 `DisableParallelization = true`
- 避免把不相關的慢測試放進過寬的 serial lane；如果較小的 collection 就能保留隔離性，應讓其他 lanes 可以同時執行

## 涉及 installer 與 client registration 的變更

Installer 驗證必須同時涵蓋 registration metadata 與可執行的 MCP server 契約：

1. 確認產生的 artifacts 符合目標 client schema：VS Code 與 Visual Studio 使用 `servers`；Cursor、Claude Desktop 與 generic MCP clients 使用 `mcpServers`；Claude Code 與 Codex artifacts 使用各自文件化的 CLI 指令
2. 確認每個產生的 `command` 值都是絕對路徑，且指向已安裝的 `wpf-devtools-<arch>.exe`
3. 從 registration entry 啟動已安裝 executable 的 STDIO MCP server，並確認 MCP `initialize` 加上 `tools/list` 流程成功

這能避免 installer 寫出看似合理的設定，但已安裝 package 實際上無法被 MCP client 啟動的回歸。

## 好的回歸測試應具備什麼特徵

- 在修復前會失敗
- 在最小修復後會通過
- 保護的是真實行為契約，而不是單純的 mock 或 placeholder 分支
