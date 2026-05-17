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
- `recovery`，提供自動恢復指引的 canonical `recovery` object
- `errorData`

為了相容舊版 client，相同值也可能在回應中投影成這些 top-level compatibility projection fields。若 `recovery.*` 與 top-level 欄位同時存在，請優先使用 `recovery.*`：

- `hint`
- `suggestedAction`
- `requiresReconnect`
- `stateAfterTimeoutUnknown`
- `processId`
- `timeoutSeconds`
- `retryAfterSeconds`
- `retryAfter`
- `availableTokens`
- `availableEvents`

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

新的工具回應除了 `errorCode` 之外，還可能提供 canonical `recovery` object 作為可直接執行的 recovery 提示。請先讀取 canonical `recovery` object，再將 top-level compatibility projection fields 視為提供給舊版 client 的 additive mirror。

- `recovery.suggestedAction`：給人類或 agent 的明確下一步，例如 retry、reconnect，或以系統管理員權限重新啟動 MCP server。
- `recovery.requiresReconnect`：表示先前的 pipe-backed session 應視為 stale，重試前需要重新呼叫 `connect`。
- `recovery.stateAfterTimeoutUnknown`：表示 timeout 的 mutation 或 pipe-backed 操作可能已改變 target state；應先 reconnect 並重新讀取狀態，再判斷成功或失敗。
- `recovery.processId`：與 reconnect 或 timeout 提示相關的目標 process。
- `recovery.timeoutSeconds`：本次逾時所對應的 server-side timeout 預算。
- `recovery.retryAfterSeconds` 與 `recovery.retryAfter`：提供 rate limit 的 deterministic backoff 提示。

例子：

- Timeout 回應可能同時帶有 `errorCode`、`recovery.requiresReconnect`、`recovery.stateAfterTimeoutUnknown`、`recovery.processId` 與 `recovery.timeoutSeconds`，讓 client 分辨是 stale pipe、target state unknown，還是單純操作太慢。
- Rate-limit 回應可能帶有 `recovery.retryAfterSeconds` 與 `recovery.retryAfter`，方便 agent 排程重試。
- Elevation 或 access denied 回應可能將 `errorCode` 與 `recovery.suggestedAction` 配對，避免 client 只能從錯誤文字猜測下一步。

## 給 AI agent 的回報建議

當操作失敗時，請讓 agent 一併回報：

- 工具名稱
- 目標 process ID
- 完整錯誤文字
- `recovery` object，以及任何 mirror 出來的 `suggestedAction`、`requiresReconnect`、`stateAfterTimeoutUnknown` 或 `retryAfterSeconds` compatibility 欄位
- 涉及的架構
- 目前 build 是 Debug 還是 Release

這些資訊足夠讓大多數安裝與連線問題快速被定位。
