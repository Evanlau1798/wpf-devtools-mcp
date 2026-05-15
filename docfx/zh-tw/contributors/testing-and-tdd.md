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

- Unit tests：目前以 `dotnet test --no-build --list-tests` discover 到 3301 個（main unit 2978 + release-unit 323）
- Integration tests：目前以 `dotnet test --no-build --list-tests` discover 到 315 個
- 合計基準：目前以 `dotnet test --no-build --list-tests` discover 到 3616 個測試，涵蓋 unit、release-unit 與 integration suites

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

## Windows Sandbox 本機 preflight

在 release 或 native verification 相關變更消耗 hosted CI 時數前，優先使用 Windows Sandbox harness 進行本機驗證。這是 local preflight，不等同 GitHub Actions parity 保證。Sandbox runner 會以唯讀方式映射 repository，把一次性狀態寫到 `tmp/sandbox-ci`，並執行接近 CI command groups 的 PowerShell 入口；hosted workflow 仍是最後 truth。

建議的推送前 gate，範圍貼近 hosted Windows x64 managed lane：

```powershell
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode HostedWindowsX64 -ReleaseUnitShardCount 8 -UnitDebugShardCount 4 -MaxParallelLanes 4
```

若變更不需要完整 hosted Debug/Release matrix，可使用較快的 native smoke：

```powershell
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode NativeSmoke -ReleaseUnitShardCount 8 -UnitDebugShardCount 4 -MaxParallelLanes 4
```

較快的切片驗證：

```powershell
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode UnitDebug -UnitDebugShardCount 4 -MaxParallelLanes 4
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode UnitRelease -ReleaseUnitShardCount 8 -MaxParallelLanes 4
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode FullManaged -ReleaseUnitShardCount 8 -UnitDebugShardCount 4 -MaxParallelLanes 4
```

artifact-only local package preflight：

```powershell
.\scripts\tools\packaging\Publish-Release.ps1 -Configuration Debug -Architectures x64 -OutputRoot .\tmp\sandbox-ci\artifact-preflight\release
$package = Get-ChildItem .\tmp\sandbox-ci\artifact-preflight\release -Filter 'release_*_win-x64.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
.\scripts\ci\Invoke-WindowsSandboxArtifactPreflight.ps1 -PackageArchivePath $package.FullName -Architecture x64 -Client other
```

Debug package 範例是 unsigned local package smoke。當 installer、package layout，或 packaged server 啟動行為有變更時，優先使用 artifact preflight。這條路徑不會在 Windows Sandbox 內重新建置 repository，而是只映射 release archive 與小型 preflight bootstrap 目錄；接著解壓 package、執行 package-local installer、以 STDIO 啟動已安裝的 MCP server、驗證 `initialize`、`tools/list`、`resources/read` 與 `get_processes`，最後 uninstall package。除非傳入已簽章的 Release archive，否則它不能證明 signed Release gate 行為。它也不會透過產生的 client registration entry 啟動；registration metadata 仍由 installer/client registration 測試覆蓋。

artifact preflight 會在 Sandbox 內依需要 provision .NET runtime channel `8.0`，用來模擬 hosted runner 通常由 `setup-dotnet` 提供的前置條件。若要驗證不同 runtime channel 可使用 `-DotNetChannel`；只有在 Sandbox image 已有必要 runtime 時才使用 `-SkipDotNetProvisioning`。

操作注意事項：

- `HostedWindowsX64` 會在 Windows Sandbox 可可靠執行的範圍內貼近 GitLab Windows x64 fallback lane 與 GitHub hosted x64 managed test 範圍：sandbox-safe native compiler/resource/archive smoke、Debug/Release solution build、兩種 configuration 的 unit shards，以及兩種 configuration 的 release-unit shards。它不涵蓋 x86、ARM64、release packaging smoke、coverage 或 NuGet pack lanes。
- Windows Sandbox 對 native DLL link step 不可靠，Visual C++ linker/resource conversion path 可能在 sandbox 內失敗。若變更 native bootstrapper，推送前請在一般桌面建置環境驗證精確的 `.vcxproj` native DLL link 與 live integration tests。
- `NativeSmoke` 會驗證 native compile/resource/archive 覆蓋，接著執行 managed debug 與 release unit shards。它刻意略過在 Windows Sandbox 內較不穩定的 native DLL link 路徑。
- Artifact preflight 的 optional `-SmokeTargetPath` 目前只涵蓋 packaged `connect` 與 scene summary 啟動 smoke。snapshot、mutation、diff、restore 與 cleanup workflow 覆蓋仍需使用一般 integration/E2E suites。
- Launcher 預設會對 Windows Sandbox host processes 套用 host-side scheduling tuning：`AboveNormal` priority，加上關閉 execution-speed power throttling。這可降低 Intel hybrid CPU 系統把 sandbox CI 視為低 QoS、集中排到 E-core 的機率。若需要停用可加 `-SkipSandboxHostScheduling`；只有在明確知道本機核心 mask 時才使用 `-SandboxHostProcessorAffinityHex 0x...` 指定 affinity。
- 結果與 logs 會寫入 `tmp/sandbox-ci/output`；產生的 `.wsb` 與 mapped work state 都是可丟棄狀態。
- 只想檢查 sandbox 設定檔時可加上 `-GenerateOnly`，避免實際啟動 Windows Sandbox。
- 不要把 `taskkill` 當成 Windows Sandbox 的主要清理機制。請優先使用 tracked `.\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot .\tmp\sandbox-ci\output` script，讓清理明確針對 Windows Sandbox HCS compute systems。如果既有本機 worktree 已經有 `tmp\sandbox-ci\Kill-WindowsSandboxHcs.ps1`，該 ignored helper 也可用於同樣目的，但它不是 tracked source artifact。
- 如果機器剛開機且尚未啟動過 Windows Sandbox，任何無關的 HCS objects 都應視為非本次 sandbox 測試殘留；移除前請先用 `-WhatIf` 檢查候選項目。

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
