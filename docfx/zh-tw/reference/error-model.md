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

## 與 injection 相關的失敗

重要的 injection 階段結果包含：

- `ArchitectureMismatch`
- `BootstrapFailed`
- `PipeReadyTimeout`

### 解讀方式

- **Architecture mismatch**：改用與目標行程 bitness 一致的 server/bootstrapper build。
- **Bootstrap failed**：bootstrapper 已啟動，但在 inspector 完全 ready 之前就失敗。
- **Pipe ready timeout**：managed bridge 可能已被呼叫，但 named pipe 沒有在時限內 ready。

## 給 AI agent 的回報建議

當操作失敗時，請讓 agent 一併回報：

- 工具名稱
- 目標 process ID
- 完整錯誤文字
- 涉及的架構
- 目前 build 是 Debug 還是 Release

這些資訊足夠讓大多數安裝與連線問題快速被定位。
