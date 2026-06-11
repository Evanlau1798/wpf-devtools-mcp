# Style 與 Template 工具

當畫面結果取決於 Style、Template、Trigger 或 ResourceDictionary 行為時，使用這組工具。所有工具都需要 active connection；請先呼叫 `connect()`，或傳入已連線的 `processId`。

## `get_applied_styles`

用途：檢查目前套用在元素上的 Style，並說明外觀值可能來自哪裡。

參數：

- `processId`：選好 active process 後可省略。
- `elementId`：可省略，省略時檢查 root window。
- `elementIds`：可選 batch mode。`elementId` 與 `elementIds` 擇一使用。
- `compact`：可選。設為 `true` 時只回傳摘要，不展開完整 setter payload。

輸出欄位包含 `hasStyle`、`styles`、`setters`、`localResourceReferenceCount` 與 `localResourceReferences`。此工具為 read-only，不會改變 runtime state。

範例：

```json
{ "elementId": "SaveButton", "compact": true }
```

復原路徑：如果找不到元素，先用 `find_elements` 重新取得候選元素；若已知 `elementId`，也可改用 `get_element_snapshot` 做單一元素 triage。

## `get_triggers`

用途：檢查 Style 與 Template trigger，找出可能條件式設定 property 的規則。

參數：

- `elementId`：必填。
- `processId`：選好 active process 後可省略。

輸出欄位包含 `triggers`、`triggerType`、`conditions` 與 `setters`。此工具只做診斷，不會觸發 event trigger，也不會修改目標程式。

範例：

```json
{ "elementId": "SaveButton" }
```

復原路徑：若 trigger 無法解釋目前值，針對特定 DependencyProperty 接著呼叫 `get_dp_value_source`。

## `get_resource_chain`

用途：從指定元素或 root window 開始，追蹤 runtime resource key 的 ResourceDictionary lookup chain。

參數：

- `resourceKey`：必填。
- `processId`：選好 active process 後可省略。
- `elementId`：可選起點。省略時從 root window 開始。

輸出欄位包含 `found`、`chain`、`level`、`dictionarySource` 與 `value`。此工具為 read-only，但可能讀出目標程式內的 UI 文字或 resource value，因此只應用於已審核且 allowlisted 的 target。

範例：

```json
{ "elementId": "SaveButton", "resourceKey": "PrimaryBrush" }
```

復原路徑：如果 `found` 為 false，可用 `get_namescope` 檢查相關 element scope，或不帶 `elementId` 從 root 重新呼叫 `get_resource_chain`。

## `override_style_setter`

用途：透過套用 local value，暫時覆蓋 style setter，以驗證 runtime-only 的外觀假設。

Policy gate：destructive。Server 必須透過 `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` 允許 destructive tools，否則會 fail closed。使用前先呼叫 `capture_state_snapshot`，驗證後若需要保持 app 不變，呼叫 `restore_state_snapshot`。

參數：

- `elementId`：必填。
- `propertyName`：必填。
- `value`：必填 JSON value。
- `processId`：選好 active process 後可省略。
- `detail`：可選，允許 `compact`、`minimal`、`verbose` 或 `standard`。

輸出欄位包含 `success`、`propertyName`、`oldValue`、`newValue`、`valueType`，以及可選 mutation metadata。此變更不會寫回 XAML。

範例：

```json
{ "elementId": "SaveButton", "propertyName": "Background", "value": "Red", "detail": "verbose" }
```

復原路徑：如果 value conversion 失敗，先用 `get_dp_metadata` 確認 property 型別，再用 `get_dp_value_source` 或 `get_state_diff` 驗證實際結果。
