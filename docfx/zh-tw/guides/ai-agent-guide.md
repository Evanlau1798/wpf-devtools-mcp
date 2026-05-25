# AI Agent 使用指南

這個 server 本來就是為 AI 輔助的 WPF 偵錯與測試而設計。效果最好的 agent 會把 MCP 工具目錄當成契約來源，先做 discovery，再把 inspection 與 mutation 清楚分開。

## 建議工作流程

1. 先探索工具與 schema。
2. 確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含已審查 target 的 exact local absolute executable path；未設定或 malformed value 會在 `connect` attach 前 fail closed。
3. 先呼叫 `connect()`，若目前只有一個可見的 WPF app，讓 server 自動完成連線。
4. 若 auto-discovery 回傳多個候選，再呼叫 `get_processes(windowFilter)`，之後用 `connect(processId)` 明確指定目標。
5. 先用可直接執行的 scene-level 工具，例如 `get_ui_summary` 或 `get_form_summary` 建立場景理解，再決定是否需要完整 tree。
6. 當 scene-level 摘要仍不足時，再瀏覽 tree 或使用聚焦搜尋取得穩定的 `elementId`；只有在已取得具體 `elementId` 後，才呼叫 `get_element_snapshot(elementId)`。
7. 執行聚焦式診斷工具，並優先遵循工具回應中的 `navigation.recommended` 或 `nextSteps`。
8. 只有在必要時，才進行受控互動或 mutation。
9. 每次互動或 mutation 後，都先看該工具回應建議的 follow-up；若目前 session 有 active snapshot，通常第一步應是 `get_state_diff`。
10. 只有在需要明確健康檢查或 reconnect 驗證時，才呼叫 `ping`。

## 最佳實務

### 0. 讓 server instructions 保持 AI-friendly

請依照官方 MCP 與 Anthropic 指南的同一套原則撰寫：

- 詳細的工具描述應說明工具做什麼、適用時機、不適用時機，以及重要限制或 caveats。
- JSON schema 與 SDK annotations 只幫助 discovery，並不等於執行期驗證；tool handler 仍必須在執行期明確驗證 untrusted arguments。
- 撰寫公開 quickstart 時，優先使用真實 client workflow、prompts 與 resources，而不是 raw protocol walkthrough。

### 1. 先 discovery，再假設

不要根據過時的 prompt、截圖或記憶去硬寫參數形狀。請依 server 真正暴露出的工具 metadata 與目前 schema 動態調整。

### 2. 把 `elementId` 視為執行期狀態

`elementId` 是 session 專屬的執行期識別碼。每次都應該從目前的 tree 重新取得，而不是跨執行重複使用快取值。

### 3. 明確區分 inspection 與 mutation

inspection 工具通常可以安全地重複呼叫。mutation 工具則會直接改變執行中的 UI，必須在明確目標下使用。

若要做有狀態的驗證，建議優先使用這個順序：

1. `capture_state_snapshot`
2. 檢查後只做一次 mutation
3. 先依 `navigation.recommended` 或 `nextSteps` 驗證結果
4. 若 snapshot 仍 active，優先呼叫 `get_state_diff`
5. 如果需要保持 app 不變，再呼叫 `restore_state_snapshot`

使用高風險工具的 prompt 或範例附近，必須明確確認 local policy gates。
`connect` 前，`WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 必須包含 target 的 exact local absolute executable path。
此外，`WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS`
會 gate `click_element`、`set_dp_value`、`capture_state_snapshot`、
`restore_state_snapshot`、`drain_events`、`batch_mutate` 等 mutation、
interaction、render-measurement 與 session state-consuming tools；
`WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` 會
gate `element_screenshot`；`WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS` 會 gate
target UI text、DependencyProperty 與 binding values、event payloads、
tree/scene summaries 與 runtime state snapshots；`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` 會
gate `get_viewmodel` 與 `get_commands`。`execute_command` 必須同時啟用
`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` 與
`WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS`。

常見的 mutation 工具包含：

- `set_dp_value`
- `clear_dp_value`
- `modify_viewmodel`
- `override_style_setter`
- `click_element`
- `simulate_keyboard`
- `drag_and_drop`
- `focus_element`

### 4. 尊重工具語意

有些工具名稱相近，但語意其實不同：

- `click_element` 會模擬邏輯上的按鈕點擊，適合用來觸發按鈕行為。
- `fire_routed_event` 會手動引發 routed event route；它不是輸入手勢的通用替代品。
- `simulate_keyboard` 適合焦點與鍵盤狀態很重要的場景，而 `get_focus_state` 通常應該先確認一次。
- `drag_and_drop` 目前最適合明確文字 payload 的受控拖放流程。

### 5. 仔細解析結構化回應

工具呼叫可能在多個層級失敗：

- MCP protocol 層
- Tool execution 層
- Inspector response 層
- Injection/bootstrap 層

當回應中有以下欄位時，請優先檢查：

- `success`
- `error`
- `errorCode`
- `errorData`
- `diagnosticKind`
- `sourceKind`

另外，請優先使用 MCP 的 discovery 入口，而不是靠記憶硬猜：

- prompts，例如 `debug_binding_issue`
- resources，例如 `wpf://capabilities`

某些 client 可能會把它們顯示成 `/mcp__wpf-devtools__debug_binding_issue` 或 `@wpf-devtools:capabilities` 這類 client-specific shortcut，但可攜的標準契約仍然是 prompt 名稱與 resource URI 本身。

當工具回應包含下列欄位時，也應一併解析：

- `nextSteps`
- `navigation.recommended`
- `navigation.alternatives`
- `navigation.prefetchTools`
- `navigation.contextRefs`

`nextSteps` 是相容舊版 client 的欄位；新的 client 應以 `navigation.recommended` 為主，再把 `alternatives` 視為人工判斷時的備選路徑。

如果你已經知道下一步是什麼，具備額外 optional args 傳遞能力的 client 可在 `get_binding_errors` 呼叫傳入 `navigation=false`，省略該次回應中的 `nextSteps` 與 `navigation`，以減少 token 消耗；schema-driven client 可以在這個工具上依賴該 opt-out，因為它今天已經明確公告在 tool schema 中，但不應假設其他工具也支援它，除非 schema 也有明確公告。

### 6. 先用 scene-level 聚合，再考慮 screenshot 或大型 tree

目前最有效率的 agent 流程，通常先從這些工具開始：

- `get_ui_summary`：快速取得語義化畫面上下文
- `get_element_snapshot(elementId)`：已取得具體 `elementId` 後，針對單一元素做聚合診斷
- `get_form_summary`：表單輸入與送出狀態總覽
- `get_state_diff`：互動或 mutation 後的 before/after 差異

只有在需要精確結構時，才改用 tree 類工具，而不是一開始就展開整棵 visual tree。

### 7. 預設優先使用 compact diagnostics

- `get_binding_errors` 預設採用 `compact=true`；除非你真的需要完整逐筆 message，否則維持預設值。
- 如果你已經知道可疑的 binding path 或 property，請先用 `get_affected_elements`，再決定是否要做大範圍 recursive binding inspection。
- 如果你需要明確讀出 buffered `BindingError`、`DpChange` 或 validation event，不要只依賴機會式 piggyback，請改用 `drain_events`。

### 8. 有序 mutation 請明確使用 orchestration

若工作流需要多個有順序的 live mutation，請優先使用 `batch_mutate`，而不是在單一 reasoning step 中臨時拼接多個 destructive 呼叫。這樣能保留每一步的結果，也更容易用 `get_state_diff`、`drain_events` 或其他聚焦工具驗證。

## 容易成功的提示模式

### 先看 scene 的提示詞

```text
先確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含 WPF 測試應用程式的 exact local absolute executable path；未設定或 malformed value 會在 `connect()` attach 前 fail closed。接著用 `connect()` 連線，呼叫 `get_ui_summary(depthMode: "semantic")` 建立語義上下文，只有在摘要不足時才展開 visual tree。
```

### Binding triage 提示詞

```text
先確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含目標 WPF 應用程式的 exact local absolute executable path；未設定或 malformed value 會在 `connect()` attach 前 fail closed。接著用 `connect()` 連線，以 compact 預設檢查 binding errors，再用 `get_affected_elements` 或在已辨識具體失敗元素後呼叫 `get_element_snapshot(elementId)` 檢查失敗 path，說明哪些 bindings 失敗以及原因。除非修復流程真的需要，否則不要修改 UI。
```

### 安全互動提示詞

```text
先確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含目標 WPF 應用程式的 exact local absolute executable path，且點擊前已設定 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`；未設定或 malformed value 會在 `connect()` attach 前 fail closed。接著用 `connect()` 連線，對目標表單呼叫 `get_form_summary` 或 `get_interaction_readiness`，再找到 Save 按鈕、確認 command metadata、點擊、視需要排空 buffered runtime event，最後回報 `get_state_diff` 結果。
```

### 可回復 mutation 提示詞

```text
先確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含目標 WPF 應用程式的 exact local absolute executable path，且 mutation 前已設定 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`；未設定或 malformed value 會在 `connect()` attach 前 fail closed。接著用 `connect()` 連線並建立 state snapshot，找到目標控制項後執行一次 UI mutation，或用 `batch_mutate` 做有順序的 mutation 序列，再用 `get_state_diff` 驗證結果，最後在結束前還原 snapshot。
```

## 常見反模式

- 重複使用舊 session 的 `elementId`。
- 還沒確認目標元素就先呼叫 mutation 工具。
- 對可能需要回復的 mutation 流程略過 `capture_state_snapshot`。
- 把 `fire_routed_event` 當成等價於真實使用者輸入。
- 在 auto-discovery 有歧義時，未經 `get_processes(windowFilter)` 驗證就假設目標一定是 x64。
- `connect` 失敗時忽略架構與 bootstrapper 需求。

## 自動化的黃金順序

若要做端到端自動化驗證，建議盡量遵守以下順序：

1. 確認 `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` 已包含目標的 exact local absolute executable path；未設定或 malformed value 會在 `connect()` attach 前 fail closed
2. `connect()`
3. 若需要，再呼叫 `get_processes(windowFilter)` 並使用 `connect(processId)`
4. `get_ui_summary` 或 `get_form_summary`
5. 一個或多個聚焦式診斷工具；只有在已取得具體 `elementId` 後，才呼叫 `get_element_snapshot(elementId)`
6. 一次只做一個 mutation 或 interaction
7. 先遵循工具回應中的 `navigation.recommended` 或 `nextSteps`
8. 若目前 session 有 active snapshot，優先呼叫 `get_state_diff`
9. 若目前 session 有 buffered runtime event，呼叫 `drain_events`
10. 其餘情況再用其他聚焦 verification tool 補強

這能讓失敗點更容易定位，也讓 agent trace 更值得信任。
