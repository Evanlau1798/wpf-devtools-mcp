# Bootstrap 與 Injection

## Scope: raw injection path only

這頁只描述 native bootstrapper 與 raw injection path。

當你擁有 target application 時，prefer SDK-hosted reuse。raw injection remains the fallback path for zero-instrumentation diagnostics。

如果 target app 已透過 `InspectorSdk.Initialize()` 啟動 inspector，或 packaging 與 publish mode 阻擋 raw injection，請先看[相容性矩陣](compatibility-matrix.md)與[安全模型](security.md)。

`connect()` 一定會先嘗試重用 compatible SDK-hosted Inspector，才 fallback 到 bootstrapper-based injection。

## 為什麼需要 bootstrapper

server 不會直接把 inspector 載入目標行程，而是先注入 native bootstrapper，再由 bootstrapper 針對目標 runtime 啟動正確的 managed entrypoint。

這個設計可以收緊 success contract，並讓 runtime 專屬的啟動流程更明確。

## `connect` 的高階流程

1. MCP client 在一般情況下呼叫 `connect()`；若目標已明確選定，則呼叫 `connect(processId)`。
2. server 先嘗試重用 compatible SDK-hosted Inspector。
3. 如果 SDK-host reuse 不可用，server 驗證目標行程與候選 DLL 路徑。
4. injector 驗證架構相容性。
5. 將 native bootstrapper 載入目標行程。
6. bootstrapper 依目標 runtime 選擇正確的 managed bridge。
7. 啟動 inspector。
8. server 等待目標 named pipe 變成 ready。
9. 只有在 ready 確認後才建立 session。

## Success contract

成功注入的定義比「remote thread 有回傳」更嚴格。

目前實作會區分以下幾種狀態：

- bootstrap execution 成功
- pipe 真的 ready
- session 建立成功

這點非常重要，因為如果 bootstrap 只做了一半，卻從來沒有暴露可用的 named pipe，就不應該被視為已連線 session。

## 架構規則

關鍵不只是 inspector 相容性。對 remote loading 流程來說，injector 與目標行程也必須相容。

當你看到 architecture mismatch 時，通常真正的修復方式是：

- 切換到與目標 bitness 相符的 server/bootstrapper build
- 而不是「把 inspector 改成 AnyCPU 再重編一次」

## Debug 與 Release 的 DLL 驗證差異

- **Debug**：trusted local roots 可略過簽章驗證。
- **Release**：強制執行簽章驗證。
- **Untrusted paths**：會在 path validation 階段直接拒絕。

## 你可能看到的診斷結果

常見的 injection 階段失敗包含：

- architecture mismatch
- bootstrap failure
- pipe readiness timeout

請參考[錯誤模型](../reference/error-model.md)了解它們在工具回應層會如何呈現。
