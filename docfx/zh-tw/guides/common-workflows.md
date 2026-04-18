# 常見工作流程

這些流程是穩定的人工判讀基線，不是要取代工具回應中的 `navigation` 或 `nextSteps`。當工具已經回傳後續建議時，請優先遵循該回應；當你需要補上背景脈絡或人工判斷時，再回到這些工作流程。

## 診斷 binding 失敗

1. `connect`
2. `get_binding_errors`
3. 先依 `navigation.recommended` 或 `nextSteps` 決定下一步
4. 常見 follow-up 是 `get_affected_elements`、`get_bindings`、`get_element_snapshot`、`get_binding_value_chain`
5. 若仍不清楚 source 來源，再呼叫 `get_datacontext_chain`
6. 若需要重新觸發評估，再呼叫 `force_binding_update`
7. 若 mutation 後需要明確讀出 buffered binding 或 validation event，再呼叫 `drain_events`

當 UI 看起來不對，但問題可能來自過時的 binding path、缺少 `DataContext`、converter 失敗或 source chain 無效時，請使用這個流程。若 `get_binding_errors` 或 `get_bindings` 已經明確指出下一步，請優先沿著該工具回應繼續走，而不是機械式地把整串工具全部跑完。`get_binding_errors` 預設應維持 compact 模式，只有在精簡資訊不夠時才要求 verbose。

## 檢查 visual subtree

1. `connect`
2. `get_ui_summary`
3. `find_elements`
4. `get_visual_tree`
5. `get_logical_tree`
6. `get_namescope`
7. `get_template_tree`
8. `compare_trees`

當 template 產生的元素或 content presenter 讓 logical view 與 visual view 不一致時，這是最有效的工作流。先取得 scene summary，再決定是否真的需要展開 tree。

## 分析 dependency property 優先順序

1. `connect`
2. `get_dp_value_source`
3. `get_dp_metadata`
4. `get_applied_styles`
5. `get_resource_chain`
6. `get_triggers`

當某個屬性值不是從你預期的來源來時，請用這組工具交叉比對 precedence、style、resource 與 trigger。

## 安全的互動驗證

1. `connect`
2. 先用 `get_ui_summary`、`get_element_snapshot` 或 `get_interaction_readiness` 確認場景與目標
3. 若有需要，再用 tree、binding 或 command 工具補足細節
4. 使用 `click_element`、`simulate_keyboard` 或 `drag_and_drop`
5. 優先遵循工具回應中的 `navigation.recommended` 或 `nextSteps`
6. 若當前 session 有 active snapshot，通常應先呼叫 `get_state_diff`
7. 若當前 session 有 buffered runtime event，第一個明確的 event verification 步驟是 `drain_events`
8. 若沒有 snapshot，則用 `get_interaction_readiness`、`get_element_snapshot`、`get_dp_value_source` 或 scoped `get_ui_summary` 驗證結果

## 搭配 snapshot 的可回復 mutation 流程

1. `connect`
2. `capture_state_snapshot`
3. 用 scene-level 工具或其他診斷工具確認目標
4. 套用單一 mutation，例如 `set_dp_value`、`modify_viewmodel` 或 `override_style_setter`，或在需要有順序的多步驟時使用 `batch_mutate`
5. 優先呼叫 `get_state_diff`
6. 若需要明確讀出 buffered binding、DP 或 validation event，呼叫 `drain_events`
7. 若工具回應提供更精確的 `navigation.recommended`，沿著該建議做補充驗證
8. 如果需要回到原狀，呼叫 `restore_state_snapshot`

當你在正式環境除錯、示範或驗證時，希望實驗結束後讓 app 回到原始狀態，這是最穩健的流程。只要 snapshot 仍然 active，`get_state_diff` 應該是 mutation 後的第一優先驗證工具。

## 焦點敏感的多視窗工作流

1. `connect`
2. `get_windows`
3. `get_focus_state`
4. 對目前視窗或目標視窗呼叫 `get_visual_tree`
5. 若目標控制項尚未取得焦點，先呼叫 `focus_element`
6. 再做 `simulate_keyboard` 或其他依賴焦點的互動
7. 用 `get_focus_state` 與其他診斷工具一起驗證結果

當快捷鍵、Enter/Tab 行為、預設按鈕或對話框焦點歸屬會影響結果時，請使用這個流程。

## Layout 與效能 triage

1. `connect`
2. `get_layout_info`
3. `get_clipping_info`
4. `invalidate_layout`
5. `get_visual_count`
6. `measure_element_render_time`
7. `get_render_stats`
8. `find_binding_leaks`

當 UI 顯示錯亂、被裁切，或操作起來很卡時，建議依照這個順序逐步縮小問題範圍。
