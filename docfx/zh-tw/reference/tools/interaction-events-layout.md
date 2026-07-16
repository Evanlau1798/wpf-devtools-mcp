# 互動、事件、版面配置與效能工具

## Interaction

- `click_element`
- `drag_and_drop`
- `scroll_to_element`
- `simulate_keyboard`
- `element_screenshot`
- `get_focus_state`
- `focus_element`

當流程和鍵盤輸入、預設按鈕、tab 導覽或多視窗切換有關時，`get_focus_state` 與 `focus_element` 會很重要。

對 `focus_element` 與 `simulate_keyboard`，請從 active rendered visual tree 中選擇 visible、enabled、focusable 的控制項。若某個 real-project target 回 `ElementNotLoaded` 或無法取得鍵盤焦點，先用 `get_interaction_readiness`，或在已取得具體 `elementId` 後呼叫 `get_element_snapshot(elementId)` 檢查，再換另一個已載入且可 focus 的候選元素重試，不要只因單一候選失敗就判定 workflow 有限制。

`element_screenshot` 預設使用 `outputMode: "metadata"`，也支援 `"file"` 或 `"base64"`。Metadata 回應會包含 dimensions、`format`、`rendered: false`、`byteLength: 0`，並在需要 pixel evidence 時提供以 `outputMode: "file"` 重呼叫的 `nextSteps`，不會 render PNG bytes。metadata mode 不會回傳 `screenshotId`、`resourceUri` 或 `wpf://screenshots/{screenshotId}` handle。

File 與 base64 回應會 render pixels，包含 `rendered: true`、dimensions、`format` 與 `byteLength`。File mode 會回傳 `screenshotId`、`resourceUri`、精確的 `resourceRead` request、`fileName`、`expiresAtUtc`、`localPathRedacted: true` 與 `sha256`；base64 mode 只會在小型 inline PNG payload 時回傳 `base64Image`。較大的截圖請使用 file mode，讓 client 取得 session-scoped resource handle，而不是 inline pixels。不要傳入 `outputPath`；client 應在相同 MCP server session 以 `resourceRead.method` 和 `resourceRead.params` 讀取。如果 client 截斷 blob，請依 `resourceRead.chunking.uriTemplate` 使用連續 offset，每次不得超過 `maxChunkBytes`，組合解碼後的 byte ranges，最後以 `byteLength` 與 `sha256` 驗證。

需要證明 screenshot resource lifecycle 的 validation agent 應使用 `outputMode: "file"` 後再呼叫 `resources/read`；metadata mode 是刻意不 render 的 shape/availability probe。File mode 是 MCP server-owned retained screenshot resource。`SessionManager` 會提供 per-process server-issued lease root、在 24 小時後到期、將每個 MCP server session 限制在最多 100 筆、刪除 evicted 或 expired PNG files，並在 target session disconnect 或 server session manager dispose 時清除。此 lifecycle 由 `SessionManager` 管理，not by the Inspector default screenshot cache。

## 狀態快照與批次 mutation

- `capture_state_snapshot`
- `batch_mutate`
- `restore_state_snapshot`

這三個 tool 實際註冊在 State/Mutation 類別下（參見 `src/WpfDevTools.Mcp.Server/McpTools/StateMcpTools.cs` 與 `MutationBatchMcpTools.cs`）。之所以和 Interaction 一起列出，是因為它們是 destructive UI 互動的首選保護機制。

當你要做可能需要回復的 UI mutation 時，建議先用 `capture_state_snapshot`，結束後再視需要呼叫 `restore_state_snapshot`。

Mutation success response 可能包含 `restoreRequired: true`、`restoreStatus: "notRestored"` 與 `restoreSuggestedAction`。這代表工具已改變 runtime state，server 並不會自動幫你還原。若 app 必須保持不變，且目前有 active snapshot，先用 `get_state_diff` 驗證，再呼叫 `restore_state_snapshot`。

當你需要在單一工具呼叫中執行有順序的多個 live mutation 時，請使用 `batch_mutate`。它比在同一個 agent step 中臨時拼接多個 destructive 呼叫更安全，因為 server 會明確驗證並按順序執行每個操作。

互動類工具的回應現在也會帶出 `nextSteps` 與 `navigation`。當回應已提供 follow-up guidance 時，請優先遵循它，而不是回到固定的手工驗證清單。

## Routed Events

- `trace_routed_events`
- `get_event_handlers`
- `fire_routed_event`
- `drain_events`

`fire_routed_event` 對 route 分析很有用，但它不是所有真實使用者輸入的通用替代品。

若你先用 `trace_routed_events(mode: "start")` 建立 trace session，再做互動，後續通常應先呼叫 `drain_events` 明確讀回 buffered event records。`trace_routed_events(mode: "get")` 仍可用來讀取 trace session，但當 session 內也可能存在 binding、dependency property 或 validation event 時，`drain_events` 是較建議的 shared-buffer read path。

Start mode 預設會保留安全的 30 秒下限。若 `effectiveDuration` 大於 `requestedDuration`，回應的 `nextSteps` 會提供精確的 `trace_routed_events` 重試，保留原本公開參數 `durationMs` 並設為 `allowShortStartDuration=true`。只有在確實需要較短時窗時才使用該重試；否則保留較安全的 effective duration。

當你需要控制 trace payload 大小時，可在 `trace_routed_events(mode: "get")` 或 capture-mode retrieval 使用 `maxEvents`。Trace 回應會提供 `returnedEventCount`、`totalEventCount`、`eventsTruncated` 與 `maxEvents`，讓 agent 能判斷 `events` 陣列是否為刻意截斷，並只在需要時用更大的 cap 重試。

Trace 回應也會在 trace teardown 延遲或復原時帶出 cleanup state。請搭配 `cleanupState`、`cleanupFailed` 與 `cleanupIncomplete` 判斷：`deferredCompleted` 代表先前 cleanup 問題已復原且 handler 已移除；`deferredPending`、`deferredFailed` 或 `failed` 則表示開始下一個 trace 前要更謹慎。

部分互動與診斷回應也可能 piggyback 精簡版 `pendingEvents`。若你需要完整且顯式的 event read step，而不是機會式 piggyback，請改用 `drain_events`。

## Layout

- `get_layout_info`
- `highlight_element`
- `get_clipping_info`
- `invalidate_layout`

`get_clipping_info` 會分析一個具體目標及其 visual ancestors，也可透過 `elementIds` 分析最多 100 個明確目標。它會辨識明確設定的 `Clip`、`ClipToBounds` 與 WPF 自動產生的 layout clip，並回傳 `clippingSource`、各方向的 `overflowAmount`、實際造成裁切的 `clippingAncestors` 與通用 `suggestedFix`；batch results 會保留 `elementId` correlation。

`visibleContentImpact="not-determined"` 表示結構性的 clip 或 overflow 本身無法證明可見 pixel 已遺失。變更 layout 前，先對受影響內容進行 focused descendant checks 或取得 screenshot 確認。

此 tool 不會自動彙總容器內所有 descendants。若被裁切的 caption 或 control 沒有名稱，先以 `find_elements(query: "可見文字")` 找到 IDs，再傳給 `get_clipping_info`。`diagnose_visibility` 也會使用相同的有效裁切邊界判斷 partial 或 full clipping。

## MVVM

- `get_viewmodel`
- `get_commands`
- `execute_command`
- `modify_viewmodel`
- `get_validation_errors`

## Performance

- `get_render_stats`
- `find_binding_leaks`
- `measure_element_render_time`
- `get_visual_count`

Render statistics 回應預設會把內部 visual-count walk 限制在 1000 個節點。使用 `visualCountLimit` 與 `visualCountTruncated` 判斷 render stats 內的 visual count 是完整值，還是已被刻意截斷。

## 安全使用模式

1. 先檢查。
2. 在改變 UI 狀態前先呼叫 `capture_state_snapshot`。
3. 若流程受焦點影響，先用 `get_focus_state` 與 `focus_element` 確認目標。
4. 做一次互動，或在需要有順序 mutation 時使用 `batch_mutate`。
5. 先依工具回應中的 `navigation.recommended` 或 `nextSteps` 驗證。
6. 若目前 session 有 active snapshot，通常應先呼叫 `get_state_diff`。
7. 若目前 session 有 buffered runtime event，通常應先呼叫 `drain_events`。
8. 若需要回復或保持 app 不變，呼叫 `restore_state_snapshot`。
9. 除非 `batch_mutate` 是刻意選擇的 orchestration tool，否則避免在單一步驟中疊很多彼此獨立的 mutation。
