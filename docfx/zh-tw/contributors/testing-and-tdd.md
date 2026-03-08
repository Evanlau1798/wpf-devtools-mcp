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

## 涉及 MCP workflow 的變更

當工具語意或 server 行為改變時，建議用以下順序驗證：

1. unit tests
2. integration tests
3. 針對測試應用程式執行 live MCP smoke harness

## 好的回歸測試應具備什麼特徵

- 在修復前會失敗
- 在最小修復後會通過
- 保護的是真實行為契約，而不是單純的 mock 或 placeholder 分支
