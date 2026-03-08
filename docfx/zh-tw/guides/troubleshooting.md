# 疑難排解

## `connect` 立刻失敗

請先確認：

- 目標行程是正在執行中的 WPF 應用程式。
- MCP server 的架構與目標行程架構一致。
- 對應架構的 native bootstrapper 已建置完成。
- inspector DLL 路徑位於 trusted root 之內。
- 若是生產環境，inspector DLL 已完成 Authenticode 簽署。

## `connect` timeout

timeout 不一定代表同一種錯誤。失敗可能出在以下任一階段：

- bootstrapper 沒有成功載入
- runtime detection 失敗
- managed bridge invocation 失敗
- inspector 已被呼叫，但 named pipe 始終沒有 ready

如果錯誤訊息指出是 pipe readiness，請把它視為啟動就緒問題，而不只是 transport 問題。

## Architecture mismatch 錯誤

大多數情況下，正確解法是改用與目標行程 bitness 相符的 server/bootstrapper build。

- x64 target -> x64 server/bootstrapper
- x86 target -> x86 server/bootstrapper
- ARM64 target -> ARM64 bootstrapper 與相容環境

不要假設 AnyCPU inspector 就能消除 injector bitness 的限制。

## `get_event_handlers` 回傳 0 handlers

零處理器是有效結果，與「平台不支援」不同。目前實作可以理解 .NET 8 的 `EventHandlersStore` 成員形狀；若沒有附加任何 handler，就會回傳空結果。

## `element_screenshot` 失敗或 bounds 為空

請確認該元素真的已經完成 render，而且 visual bounds 不為 0。某些 template part 或延遲建立內容可能需要先捲動到可見區域，或先觸發版面配置。

## `drag_and_drop` 沒有更新目標控制項

建議先從文字 payload 的拖放場景開始。拖放後，再用 `get_dp_value_source` 等 inspection 呼叫驗證結果，例如檢查 `Text` 屬性是否真的改變。

## 驗證或 TLS 相關失敗

若你開啟了安全加固設定：

- 確認 `WPFDEVTOOLS_AUTH_SECRET` 在兩端都可取得
- 確認憑證資料夾可讀、可寫且具持久性
- 若有做 thumbprint pinning，確認設定值正確

## 下一步該看哪裡

- [安全模型](../production/security.md)
- [Bootstrap 與 Injection](../production/bootstrap-and-injection.md)
- [設定參考](../reference/configuration.md)
- [錯誤模型](../reference/error-model.md)
