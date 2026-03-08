# 相容性矩陣

## Runtime 相容性

| 目標應用程式 | Inspector runtime 路徑 | 備註 |
| --- | --- | --- |
| .NET Framework WPF | `net48` inspector path | 需要匹配的 bootstrapper 架構 |
| .NET 6/7/8+ WPF | `net8.0-windows` inspector path | 需要匹配的 bootstrapper 架構 |

## 架構相容性

| 目標行程架構 | 建議的 server/bootstrapper build | 備註 |
| --- | --- | --- |
| x86 | x86 / Win32 | 跨 bitness 安全性的必要條件 |
| x64 | x64 | 大多數現代 WPF 應用的預設選擇 |
| ARM64 | ARM64 | 僅用於原生 ARM64 目標 |

## 已知不支援或受限的情境

| 情境 | 狀態 | 原因 |
| --- | --- | --- |
| Self-contained single-file WPF app | 不支援 | Native injection 路徑無法依賴預期的組件配置 |
| Native AOT | 不支援 | Managed runtime hosting 的前提不成立 |
| Trimmed app | 部分支援 / 風險較高 | 必要型別可能被裁掉 |
| 非 WPF 桌面 UI 技術 | 不支援 | 本 server 明確只針對 WPF |

## 實務建議

- 以 `get_processes` 回傳的 architecture 當作唯一真相來源。
- 把 x86 與 x64 視為兩個不同部署目標，不要混用。
- 在自動化中呼叫 `connect` 前，先驗證 bootstrapper 與 inspector 選擇是否正確。
