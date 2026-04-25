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

目前專案層級的驗證狀態以下列最近一次完整完成的 full-suite 為準。

### 測試結果

- Unit tests：總數 2767，通過 2767，失敗 0
- Integration tests：總數 289，通過 289，失敗 0
- Full-suite 總計：3056 個測試，通過 3056，失敗 0

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

## 好的回歸測試應具備什麼特徵

- 在修復前會失敗
- 在最小修復後會通過
- 保護的是真實行為契約，而不是單純的 mock 或 placeholder 分支
