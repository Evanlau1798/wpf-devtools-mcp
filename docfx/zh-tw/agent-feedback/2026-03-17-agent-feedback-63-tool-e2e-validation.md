# Agent 回饋：63 工具端對端驗證

## 背景

- Agent：Claude Opus 4.6（Claude Code CLI）
- 日期：2026-03-17
- 應用程式 / 場景：自訂邊際案例 WPF 應用程式（tmp/EdgeCaseApp），涵蓋繫結錯誤、可見性邊際案例、MultiBinding、DataContext 切換、裁切、多視窗、停用命令及可變 ViewModel 狀態
- 建置 / 發行版本：本機開發建置（master 分支）

## 測試的工作流程

1. 連線與程序探索（connect、get_processes、get_active_process、select_active_process、ping）
2. 場景優先調查（get_ui_summary、get_form_summary、diagnose_visibility、get_interaction_readiness、取得 elementId 後再使用 get_element_snapshot）
3. 完整 63 工具驗證，涵蓋所有 10 個工具類別的跨工具工作流程（繫結錯誤分類、快照/差異/還原、batch_mutate、運算式回復、路由事件追蹤、焦點敏感鍵盤互動）

## 運作良好的部分

- **場景優先工具完全消除了對截圖的依賴。** `get_ui_summary(semantic)` 回傳了 82 個帶註解的節點，內嵌 `[visibility:Hidden]`、`[disabled]`、`[transparent]` 標記 — 我完全不需要截圖就能理解應用程式狀態。
- **導航提示（`navigation.recommended`）始終建議正確的下一步工具**，且預先填入參數。`get_binding_errors` 之後，它為確切的失敗元素推薦了 `get_datacontext_chain`。`capture_state_snapshot` 之後，它以正確的 snapshotId 推薦了 `get_state_diff`。這將我的規劃開銷降至接近零。
- **變更/互動回應中附帶的事件**節省了往返次數。`click_element(SaveButton)` 的回應中同時包含了 StatusText 上的 DpChange 和 RoutedEvent，無需另外呼叫 `drain_events`。
- **運算式支撐的 DependencyProperty 回復運作完美。** 對繫結支撐的 TextBox.Text 執行 `set_dp_value`，回報 `replacedExpression=true, capturedRollbackExpression=true`。接著 `clear_dp_value` 以 `restoredExpression=true, expressionKind=Binding` 還原繫結。這是關鍵的安全功能。
- **快照/差異/還原週期具有確定性。** 擷取了 1 個 DP + 2 個 VM 屬性 + 焦點。經過 2 次變更和 1 次命令執行後，`get_state_diff` 偵測到所有變更。`restore_state_snapshot` 以零略過屬性、零警告還原了一切。
- **`batch_mutate` 搭配 `includeDiff=true`** 在一次呼叫中執行了 2 個循序變更並回傳整合差異 — 對多步驟工作流程而言是顯著的 token 節省。
- **`get_binding_mismatches(recursive=true)`** 正確區分了 PathMismatch（嚴重性=Warning）和 TypeMismatchWithConverter（嚴重性=Info），展現了對轉換器調和型別差異的認知。
- **`get_affected_elements(propertyName=FirstName)`** 以高信賴度偵測到 4 個受影響的元素，`matchStrategy=multibinding-child-path-match` — 找到了 FullNameDisplay 上的 MultiBinding 子路徑，與完整的值鏈追蹤一致。
- **`get_binding_value_chain` 完整追蹤 MultiBinding。** 對於使用 `MultiBinding` + `FullNameConverter` 的 TextBlock，值鏈回傳了 7 個步驟：MultiBinding 定義（含轉換器）、BindingInput[0]（FirstName="Alice"，已解析）、BindingInput[1]（LastName="Smith"，已解析）、DataContext 鏈、ResolvedSource，以及 FinalValue="Alice Smith"。這讓 Agent 能完整掌握多輸入值解析過程。
- **`diagnose_visibility`** 正確診斷了三種不同的根因（Visibility=Hidden、Visibility=Collapsed 伴隨零佈局尺寸、Opacity=0），並提供了可行的 `suggestedFix` 值。
- **`get_form_summary`** 識別了 BrokenButton 的阻擋因素（`ElementDisabled` + `CommandCannotExecute`）和 FocusTestButton 的阻擋因素（`ElementInInactiveTab`）— 這正是 Agent 在嘗試互動前所需的資訊。
- **`wait_for_dp_change` 搭配 `triggerMutation`** 對序列化的 STDIO 用戶端而言極為出色。它執行了 `modify_viewmodel(StatusMessage=WaitTest)` 並立即偵測到值變更，`elapsedMs=0`。
- **`trace_routed_events` 的 start+get 工作流程可靠地擷取了事件**，使用 5 秒視窗搭配 `allowShortStartDuration=true`。start→click_element→get 的順序正確擷取了 Click 冒泡事件。
- **`get_render_stats(warmUp=true)`** 在單次呼叫中回傳了高信賴度資料（30 個樣本、frameRate=35.31、avgRenderTime=28.32ms），消除了多次呼叫暖機的需求。
- **Token 效率控制設計良好。** `compact=true`、`summaryOnly=true`、`navigation=false`、`maxNodes`、`maxChildrenPerNode` 皆如文件所述運作，且顯著減少回應大小。

## 觀察到的摩擦

- **`get_form_summary` 對於沒有明確 Label/Target 繫結的輸入控制項的標籤啟發式**會指定最近的標題作為標籤。FocusBox1、FocusBox2、FocusBox3 皆獲得 label="Focus and Keyboard Testing"（區段標題），當多個輸入共享同一推斷標籤時會造成歧義。
- **`drag_and_drop` 需要應用程式端的拖放處理程式**才能產生可見效果。工具正確模擬了拖放事件序列（dropped=true），但目標應用程式中沒有處理程式時不會有可觀察的狀態變更。這是預期行為，但 Agent 應了解成功回應不保證應用程式已處理拖放。

## 具體範例

```text
工具呼叫：get_binding_value_chain(elementId=TextBlock_1, propertyName=Text)
結果：{
  hasBinding: true,
  chainLength: 7,
  chain: [
    { step: "Binding", bindingType: "MultiBinding", converter: "FullNameConverter", bindingPaths: ["FirstName","LastName"] },
    { step: "BindingInput", bindingIndex: 0, path: "FirstName", value: "Alice", resolutionState: "Resolved" },
    { step: "BindingInput", bindingIndex: 1, path: "LastName", value: "Smith", resolutionState: "Resolved" },
    { step: "LocalDataContext", dataContextType: "MainViewModel" },
    { step: "InheritedDataContext", dataContextType: "MainViewModel" },
    { step: "ResolvedSource", sourceType: "MultiBinding", resolutionState: "Resolved" },
    { step: "FinalValue", value: "Alice Smith", valueType: "String" }
  ]
}
評估：完整的 MultiBinding 值解析在一次呼叫中可見。
```

```text
工具呼叫：trace_routed_events(mode=start, eventName=Click, durationMs=5000, allowShortStartDuration=true)
  接著：click_element(elementId=Button_2)
  接著：trace_routed_events(mode=get)
結果：{ eventCount: 1, events: [{ sender: "Button", senderName: "ResetButton", eventName: "Click", routingStrategy: "Bubble" }] }
評估：5 秒視窗搭配 allowShortStartDuration=true 對 Agent IPC 往返已足夠。
```

```text
工具呼叫：get_render_stats(warmUp=true)
結果：{ isWarmedUp: true, confidence: "high", sampleCount: 30, frameRate: 35.31, avgRenderTime: 28.32 }
評估：單次呼叫暖機即產生高信賴度的渲染指標，無需多次呼叫模式。
```

## 改善建議

- 對於 `get_form_summary`，當標籤啟發式將區段標題指定給多個輸入時，考慮附加輸入的 x:Name 或索引以消除歧義（例如 "Focus and Keyboard Testing > FocusBox1"）
- 對於 `drag_and_drop`，考慮回報目標元素是否有已註冊的 Drop/DragOver 處理程式，讓 Agent 能預判拖放是否會被處理

## 優先順序評估

- P0：（無 — 全部 63 個工具皆正確運作）
- P1：（無）
- P2：表單標籤消歧義（共享區段標題的輸入）；drag_and_drop 處理程式存在提示

## Token / 承載量觀察

- `get_ui_summary(semantic, depth=3)` 的 82 個節點：結構良好但回應較大。當只需要 summaryText 時，`summaryOnly=true` 顯著減少承載量。
- `navigation=false` 確認會移除 `navigation` 和 `nextSteps` — 對已知下一步的 Agent 而言有可衡量的 token 節省。
- `batch_mutate` 搭配 `includeDiff=true` 回傳的合併回應，否則需要 4 次獨立呼叫（擷取 + 2 次變更 + 差異）。顯著的 token 效率提升。
- 變更回應中附帶的事件（`pendingEvents`）增加少量承載量，但節省了整個 `drain_events` 的往返。
- 樹工具的 `compact=true` 和 `get_dp_value_source` 的 `compact=true` 對重複診斷模式而言顯著減少每次呼叫的 token。
- `get_binding_value_chain` 對 MultiBinding 回傳 7 步驟的值鏈 — 比簡單 Binding 鏈更豐富，但以每個輸入的 `bindingIndex` 關聯結構良好。

## 附加備註

- 整體體驗評分為 **10/10**。每個工具都正確運作，包括完整的 MultiBinding 值鏈追蹤、短視窗下可靠的路由事件擷取，以及單次呼叫暖機的效能指標。
- 回應契約（v2026-03-13-ai-friendly-v3）設計良好。`navigation.recommended` 與 `nextSteps` 的分層讓進階 Agent 能使用更豐富的封套，同時維持向後相容性。
- `contextRefs` 項目（例如 `type=binding-issue`、`type=mutation-session`）對於跨工具呼叫維護對話狀態的 Agent 很有用。
- 透過 `connect()` 無參數的自動探索是正確的預設工作流程。退回到 `get_processes` 進行消歧義的設計簡潔明瞭。
- 樹工具上的 `depthSufficiencyHint`（含 `reasonCode`、`recommendedDepth` 和 `suggestion`）是一個聰明的模式，防止 Agent 在第一次呼叫時取得不足，同時不要求過度取得。
- MultiBinding 值鏈中每個子繫結獲得獨立的 `BindingInput` 步驟搭配 `bindingIndex`，讓 Agent 能輕鬆將輸入與最終轉換器輸出進行關聯，結構特別良好。
