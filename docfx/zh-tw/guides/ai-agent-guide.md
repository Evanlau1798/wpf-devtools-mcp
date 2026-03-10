# AI Agent 使用指南

這個 server 本來就是為 AI 輔助的 WPF 偵錯與測試而設計。效果最好的 agent 會把 MCP 工具目錄當成契約來源，先做 discovery，再把 inspection 與 mutation 清楚分開。

## 建議工作流程

1. 先探索工具與 schema。
2. 呼叫 `get_processes`。
3. 呼叫 `connect(processId)`。
4. 呼叫 `ping`。
5. 先瀏覽 tree，取得穩定的 `elementId`。
6. 執行診斷工具。
7. 只有在必要時，才進行受控互動或 mutation。

## 最佳實務

### 1. 先 discovery，再假設

不要根據過時的 prompt、截圖或記憶去硬寫參數形狀。請依 server 真正暴露出的工具 metadata 與目前 schema 動態調整。

### 2. 把 `elementId` 視為執行期狀態

`elementId` 是 session 專屬的執行期識別碼。每次都應該從目前的 tree 重新取得，而不是跨執行重複使用快取值。

### 3. 明確區分 inspection 與 mutation

inspection 工具通常可以安全地重複呼叫。mutation 工具則會直接改變執行中的 UI，必須在明確目標下使用。

若要做有狀態的驗證，建議優先使用這個順序：

1. `capture_state_snapshot`
2. 檢查後只做一次 mutation
3. 立刻驗證結果
4. 如果需要保持 app 不變，再呼叫 `restore_state_snapshot`

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

- prompts，例如 `/mcp__wpf-devtools__debug_binding_issue`
- resources，例如 `@wpf-devtools:capabilities`

## 容易成功的提示模式

### 先看 tree 的提示詞

```text
連線到 WPF 測試應用程式，先檢查 visual tree 找出主要表單控制項，再在不做任何修改的前提下摘要頂層結構。
```

### Binding triage 提示詞

```text
連線到目標 WPF 應用程式，檢查 binding errors，說明哪些元素失敗以及原因。除非修復流程真的需要，否則不要修改 UI。
```

### 安全互動提示詞

```text
在目前 visual tree 中找到 Save 按鈕，先確認其 binding 與 command metadata，再點擊它並回報發生了什麼變化。
```

### 可回復 mutation 提示詞

```text
先建立 state snapshot，找到目標控制項後只做一次 UI mutation，驗證結果，最後在結束前還原 snapshot。
```

## 常見反模式

- 重複使用舊 session 的 `elementId`。
- 還沒確認目標元素就先呼叫 mutation 工具。
- 對可能需要回復的 mutation 流程略過 `capture_state_snapshot`。
- 把 `fire_routed_event` 當成等價於真實使用者輸入。
- 未經 `get_processes` 驗證就假設目標一定是 x64。
- `connect` 失敗時忽略架構與 bootstrapper 需求。

## 自動化的黃金順序

若要做端到端自動化驗證，建議盡量遵守以下順序：

1. `get_processes`
2. `connect`
3. `ping`
4. `get_visual_tree`
5. 一個或多個診斷工具
6. 一次只做一個 mutation 或 interaction
7. 每次 mutation 後都立刻做一次 verification

這能讓失敗點更容易定位，也讓 agent trace 更值得信任。
