# Injection 與 Runtime 選擇

## Runtime selection 問題

WPF 應用可能跑在 .NET Framework，也可能跑在現代 .NET。server 必須在執行期選擇正確的 inspector target framework。

## 目前做法

- 偵測目標 runtime 家族
- 偵測目標行程架構
- 選出正確的 inspector payload
- 注入 native bootstrapper
- 針對該 runtime host 正確的 managed bridge
- 等待 named pipe readiness 訊號

## 為什麼不直接注入 inspector

bootstrapper 模型能改善啟動診斷，並讓 `connect` 的 success contract 更可信。同時，它也把 runtime 專屬的 hosting 邏輯集中到單一位置。

## 給操作人員的重要規則

architecture matching 不是選配。injector/bootstrapper 路徑要求 build 與目標行程 bitness 相符。

## 給貢獻者的重要規則

請把 `connect` 視為多階段 pipeline，而不是單一步驟。錯誤處理、測試與文件都應保留以下狀態的區別：

- validation failure
- bootstrap failure
- readiness timeout
- session success
