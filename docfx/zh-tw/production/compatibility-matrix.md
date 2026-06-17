# 相容性矩陣

## Runtime 相容性

| 目標應用程式 | Inspector runtime 路徑 | 備註 |
| --- | --- | --- |
| .NET Framework WPF | `net48` inspector path | 需要匹配的 bootstrapper 架構 |
| .NET 8+ WPF | `net8.0-windows` inspector path | 需要匹配的 bootstrapper 架構 |
| .NET 6/7 WPF | 目前沒有出貨的 raw-injection inspector path | Raw injection 目前不支援；請將 target app 升級到 .NET 8+，或等待明確的 SDK / inspector target expansion |

## 架構相容性

| 目標行程架構 | 建議的 server/bootstrapper build | 備註 |
| --- | --- | --- |
| x86 | x86 / Win32 | 跨 bitness 安全性的必要條件 |
| x64 | x64 | 大多數現代 WPF 應用的預設選擇 |
| ARM64 | ARM64 | 僅用於原生 ARM64 目標 |

Architecture matching is mandatory for raw injection/bootstrapper fallback。SDK-hosted reuse 透過 named pipes 通訊；當 target-side host 已經啟動後，不要求 server process 與 target process bitness 相同。

## 已知不支援或受限的情境

### Self-contained single-file WPF app

- Raw injection 路徑：不支援。
- 整體支援姿態：可透過 SDK-host reuse 支援。
- 說明：Native injection 路徑無法依賴預期的組件配置。請在 target app 中呼叫 `InspectorSdk.Initialize()`，並讓 transport 設定一致，讓 `connect()` 可以重用既有 host。

### Native AOT

- Raw injection 路徑：不支援。
- 整體支援姿態：不支援。
- 說明：WPF DevTools 目前不支援 Native AOT targets。SDK-hosted reuse 不是 Native AOT workaround，因為 Inspector SDK 仍需要 managed WPF runtime 與 assembly access。

### Trimmed app

- Raw injection 路徑：風險較高 / 部分支援。
- 整體支援姿態：優先使用 SDK-host reuse。
- 說明：必要型別可能被裁掉，導致 raw injection 或 inspector 啟動不穩定。

### Non-WPF desktop UI

- Raw injection 路徑：不支援。
- 整體支援姿態：不支援。
- 說明：本 server 明確只針對 WPF。

## 實務建議

- 以 `get_processes` 回傳的 architecture 當作唯一真相來源。
- 把 x86 與 x64 視為兩個不同部署目標，不要混用。
- 當你擁有 target app 時，prefer SDK-hosted reuse；raw injection remains the fallback path for zero-instrumentation diagnostics。
- .NET 6/7 WPF target 目前沒有出貨的 raw-injection inspector path；不要假設 `net8.0-windows` 可以注入這些 process。
- 若 single-file packaging 會阻擋 raw injection，請在 target-side 先呼叫 `InspectorSdk.Initialize()`，並維持相符的 transport 設定，讓 `connect()` 可以重用該 SDK host。
- Native AOT 仍不支援；SDK-hosted reuse 不是 Native AOT workaround。
- 在自動化中呼叫 `connect` 前，先驗證 bootstrapper 與 inspector 選擇是否正確。
