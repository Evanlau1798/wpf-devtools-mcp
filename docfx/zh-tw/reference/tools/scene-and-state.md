# Scene 與 State 診斷工具

在 `connect()` 後，優先使用這組工具取得語意化 runtime state，再決定是否需要展開大型 tree 或截圖。除其他頁面特別標示的 state capture、mutation 或 restore workflow 外，本頁工具皆為 read-only。

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
