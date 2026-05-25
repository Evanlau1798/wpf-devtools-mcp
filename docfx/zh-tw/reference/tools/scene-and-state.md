# Scene 與 State 診斷工具

在 `connect()` 後，先用這組工具取得語意化 runtime state，再決定是否需要展開大型 tree 或截圖。建議順序是 summary、focused snapshot、readiness 或 visibility 診斷，只有需要改變 runtime state 時才進入 snapshot-backed mutation 與 diff。

## `get_ui_summary`

用途：將目前 WPF window 或 subtree 摘要成面向使用者的 semantic nodes。

參數：

- `processId`：選好 active process 後可省略。
- `elementId`：可選 subtree root。省略時摘要 root window。
- `depth`：可選 traversal budget。
- `depthMode`：可選，通常使用 `semantic`。
- `summaryOnly`：可選。agent 只需要文字上下文時設為 `true`。

輸出欄位包含 `rootElementId`、`semanticNodeCount`、`summaryText` 與 `nodes`。當目標是理解畫面，而不是精確檢查 tree 結構時，優先使用此工具。

範例：

```json
{ "depthMode": "semantic", "summaryOnly": true }
```

復原路徑：如果 summary 指出具體元素，先接著呼叫 `get_element_snapshot(elementId)`，再考慮完整 visual tree。

## `get_form_summary`

用途：摘要 form-like subtree 內的 input、label、validation 與 command readiness。

參數：

- `processId`：選好 active process 後可省略。
- `elementId`：可選 form scope。省略時檢查 root window。
- `includeFramework`：可選。維持 `false` 可排除 framework-internal template controls。

輸出欄位包含 `inputs`、`commands`、`summary`、`validationSubmittable`、`interactionSubmittable` 與 `isSubmittable`。

範例：

```json
{ "elementId": "ProfileForm_2" }
```

復原路徑：如果 form 不可送出，針對列出的 input 或 command 接著使用 `get_element_snapshot(elementId)`、`diagnose_visibility` 或 `get_interaction_readiness`。

## `get_element_snapshot`（需要 `elementId`）

用途：用單一回應取得某個 runtime element 的常用診斷資訊。

參數：

- `elementId`：必填。
- `processId`：選好 active process 後可省略。
- `includeProperties`：可選，會附加額外 DependencyProperty probes，不會取代預設集合。

輸出欄位包含 identity、selected properties、bindings、validation errors、style summary、layout summary 與 DataContext type。這是 `get_ui_summary` 或 `find_elements` 之後最常用的 drill-down。

範例：

```json
{ "elementId": "SaveButton_7", "includeProperties": ["IsEnabled", "Visibility"] }
```

復原路徑：如果特定值仍不清楚，接著使用 `get_dp_value_source`、`get_bindings`、`get_applied_styles` 或 `get_triggers`。

## `diagnose_visibility`

用途：不依賴 screenshot，解釋已知 runtime element 為什麼目前可見或不可見。

參數：

- `elementId`：必填。通常來自 `find_elements`、`get_ui_summary`、`get_visual_tree` 或其他結構化回應。
- `processId`：選好 active process 後可省略。

輸出欄位包含 `isUserVisible`、`checks`、`rootCause` 與 `suggestedFix`。常見檢查包含 `Visibility`、ancestor visibility、opacity、layout size、clipping 與是否參與 render。

範例：

```json
{ "elementId": "HiddenByAncestorText_4" }
```

復原路徑：若元素不在 active visual tree，先用 `get_element_snapshot(elementId)` 檢查 tab、virtualization 或 layout state；只有 scene tools 無法定位 active branch 時，再使用 `get_visual_tree`。

## `get_interaction_readiness`

用途：在呼叫 `click_element`、`simulate_keyboard` 或 command tool 前，判斷元素目前是否能互動。

參數：

- `elementId`：必填。
- `processId`：選好 active process 後可省略。
- `interactionType`：可選 label，預設為 `Click`。

輸出欄位包含 `isReady`、`blockers`、`interactionType` 與 `elementState`。此工具可以提供 command readiness，不需要暴露任意 ViewModel value。

範例：

```json
{ "elementId": "SaveButton_7", "interactionType": "Click" }
```

復原路徑：如果 `isReady` 為 false，先處理 `blockers`。常見 follow-up 包含 `diagnose_visibility`、`get_focus_state`、`focus_element`、`get_dp_value_source`，或在明確允許 ViewModel inspection 時使用 `get_commands`。

## `capture_state_snapshot`

用途：在 destructive testing 或多步驟 debugging 前，捕捉可復原的 runtime state。

參數：

- `processId`：選好 active process 後可省略。
- `elementId`：可選 scope。
- `propertyNames`、`viewModelPropertyNames` 與 `includeFocus` 決定要捕捉的 state。
- `snapshotName`：可選 label。

輸出欄位包含 `snapshotId` 與 `snapshotSummary`。Snapshot 存於記憶體、綁定 session，並且有保留數量與時間上限。

範例：

```json
{ "elementId": "EditorPanel", "propertyNames": ["Text"], "includeFocus": true }
```

復原路徑：保留回傳的 `snapshotId`；mutation 後用它呼叫 `get_state_diff`，需要回到 baseline 時呼叫 `restore_state_snapshot`。

## `batch_mutate`

用途：依序執行多個允許的 runtime mutation，並用單一 failure surface 呈現結果。

Policy gate：destructive。Server 必須允許 destructive tools，此工具才會執行。

參數：

- `mutations`：必填，支援的 mutation steps 陣列。
- `captureSnapshot`：可選 snapshot request。
- `includeDiff`：可選；需要搭配 `captureSnapshot`。
- `trigger`、`processId` 與預設 `elementId`：可選。

輸出欄位包含 execution counts、每個 step 的結果、可選 `snapshotId`、可選 `stateDiff` 與 rollback guidance。遇到第一個失敗 step 就停止，且不會自動 rollback。

範例：

```json
{ "captureSnapshot": { "propertyNames": ["Text"] }, "includeDiff": true, "mutations": [{ "tool": "set_dp_value", "args": { "elementId": "NameTextBox", "propertyName": "Text", "value": "Alice" } }] }
```

復原路徑：如果某個 step 失敗但前面 step 已成功，檢查 `rollback`，並在 snapshot 仍保留時呼叫 `restore_state_snapshot`。

## `get_state_diff`

用途：比較先前捕捉的 state snapshot 與目前 runtime state。

參數：

- `snapshotId`：必填，來自 `capture_state_snapshot` 或 `batch_mutate`。
- `processId`：選好 active process 後可省略。
- `trigger`：可選，用來描述 snapshot 後發生的動作。

輸出欄位包含 DependencyProperty changes、ViewModel changes、binding error deltas、validation changes、focus changes 與 duration。

範例：

```json
{ "snapshotId": "snapshot_abc", "trigger": "click_element(SaveButton)" }
```

復原路徑：如果 diff 顯示非預期變更，趁 snapshot 尚未過期時呼叫 `restore_state_snapshot(snapshotId)`。

## `restore_state_snapshot`

用途：在暫時 runtime changes 後，還原仍被保留的 state snapshot。

參數：

- `snapshotId`：必填。
- `processId`：選好 active process 後可省略。
- `removeAfterRestore`：可選，預設為 `true`。

輸出欄位包含 restored / skipped DependencyProperty values、restored / skipped ViewModel properties、focus restoration、warnings 與 verification flags。

範例：

```json
{ "snapshotId": "snapshot_abc" }
```

復原路徑：如果 restore incomplete，先檢查 skipped entries 與 verification fields 再重試。若 snapshot 過期或屬於其他 session，重新連線並重新 capture。
