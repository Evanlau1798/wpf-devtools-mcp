# ADR-005：Multi-Targeting 策略

## 狀態

Accepted。

## 背景

真實世界中的 WPF 應用同時涵蓋 .NET Framework 與現代 .NET，且可能以 x86、x64 或 ARM64 執行。

## 決策

MCP server 維持在現代 .NET；shared 與 inspector 元件在必要時採 multi-target；連線時再依目標 runtime 動態選擇正確 payload。

## 選擇原因

- 最大化對真實 WPF 應用的相容性
- 讓 server 保持在現代 runtime
- 讓 runtime 專屬 hosting 路徑清楚可見

## 影響

- 產物種類更多，測試責任也更高
- 必須更嚴格地記錄並強制執行架構匹配
- packaging 與 release artifact 需要具備架構意識

更多內容請參考[相容性矩陣](../../production/compatibility-matrix.md)。
