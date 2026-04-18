# 錯誤模型

## 失敗層級

一個工具呼叫可能在多個層級失敗：

1. MCP transport 或 protocol 層
2. Server tool execution 層
3. Inspector response 層
4. Injection/bootstrap 層

## 建議檢查欄位

若回應中有這些欄位，建議依序檢查：

- `isError`（MCP response 層）
- `success`（structured content 層）
- `error`
- `errorCode`
- `errorData`

若 client 或 agent 需要自動恢復流程，也應額外檢查這些 additive recovery 欄位：

- `suggestedAction`
- `requiresReconnect`
- `processId`
- `timeoutSeconds`
- `retryAfterSeconds`
- `retryAfter`

## 與 injection 相關的失敗

重要的 injection 階段結果包含：

- `ArchitectureMismatch`
- `BootstrapFailed`
- `PipeReadyTimeout`

### 解讀方式

- **Architecture mismatch**：改用與目標行程 bitness 一致的 server/bootstrapper build。
- **Bootstrap failed**：bootstrapper 已啟動，但在 inspector 完全 ready 之前就失敗。
- **Pipe ready timeout**：managed bridge 可能已被呼叫，但 named pipe 沒有在時限內 ready。

## Recovery contract 重點

新的工具回應除了 `errorCode` 之外，還可能提供可直接執行的 recovery 提示。

- `suggestedAction`：給人類或 agent 的明確下一步，例如 retry、reconnect，或以系統管理員權限重新啟動 MCP server。
- `requiresReconnect`：表示先前的 pipe-backed session 應視為 stale，重試前需要重新呼叫 `connect`。
- `processId`：與 reconnect 或 timeout 提示相關的目標 process。
- `timeoutSeconds`：本次逾時所對應的 server-side timeout 預算。
- `retryAfterSeconds` 與 `retryAfter`：提供 rate limit 的 deterministic backoff 提示。

例子：

- Timeout 回應可能同時帶有 `errorCode`、`requiresReconnect`、`processId` 與 `timeoutSeconds`，讓 client 分辨是 stale pipe 還是單純操作太慢。
- Rate-limit 回應可能帶有 `retryAfterSeconds` 與 `retryAfter`，方便 agent 排程重試。
- Elevation 或 access denied 回應可能將 `errorCode` 與 `suggestedAction` 配對，避免 client 只能從錯誤文字猜測下一步。

## 給 AI agent 的回報建議

當操作失敗時，請讓 agent 一併回報：

- 工具名稱
- 目標 process ID
- 完整錯誤文字
- 任何 `suggestedAction`、`requiresReconnect` 或 `retryAfterSeconds` 欄位
- 涉及的架構
- 目前 build 是 Debug 還是 Release

這些資訊足夠讓大多數安裝與連線問題快速被定位。
