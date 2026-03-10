# 常見工作流程

## 診斷 binding 失敗

1. `connect`
2. `get_binding_errors`
3. `get_bindings`
4. `get_binding_value_chain`
5. `get_datacontext_chain`
6. 若需要重新觸發評估，再呼叫 `force_binding_update`

當 UI 看起來不對，但問題可能來自過時的 binding path、缺少 `DataContext`、converter 失敗或 source chain 無效時，請使用這個流程。

## 檢查 visual subtree

1. `get_visual_tree`
2. `get_logical_tree`
3. `get_namescope`
4. `get_template_tree`
5. `compare_trees`

當 template 產生的元素或 content presenter 讓 logical view 與 visual view 不一致時，這是最有效的工作流。

## 分析 dependency property 優先順序

1. `get_dp_value_source`
2. `get_dp_metadata`
3. `get_applied_styles`
4. `get_resource_chain`
5. `get_triggers`

當某個屬性值不是從你預期的來源來時，請用這組工具交叉比對 precedence、style、resource 與 trigger。

## 安全的互動驗證

1. 先用 `get_visual_tree` 找到目標
2. 若有需要，先確認 binding 或 command
3. 使用 `click_element`、`simulate_keyboard` 或 `drag_and_drop`
4. 立刻用 `get_dp_value_source`、`get_viewmodel` 或其他 inspection 工具驗證結果

## 搭配 snapshot 的可回復 mutation 流程

1. `capture_state_snapshot`
2. 用 `get_visual_tree` 或其他診斷工具確認目標
3. 套用單一 mutation，例如 `set_dp_value`、`modify_viewmodel` 或 `override_style_setter`
4. 立刻驗證效果
5. 如果需要回到原狀，呼叫 `restore_state_snapshot`

當你在正式環境除錯、示範或驗證時，希望實驗結束後讓 app 回到原始狀態，這是最穩健的流程。

## 焦點敏感的多視窗工作流

1. `get_windows`
2. `get_focus_state`
3. 對目前視窗或目標視窗呼叫 `get_visual_tree`
4. 若目標控制項尚未取得焦點，先呼叫 `focus_element`
5. 再做 `simulate_keyboard` 或其他依賴焦點的互動
6. 用 `get_focus_state` 與其他診斷工具一起驗證結果

當快捷鍵、Enter/Tab 行為、預設按鈕或對話框焦點歸屬會影響結果時，請使用這個流程。

## Layout 與效能 triage

1. `get_layout_info`
2. `get_clipping_info`
3. `invalidate_layout`
4. `get_visual_count`
5. `measure_element_render_time`
6. `get_render_stats`
7. `find_binding_leaks`

當 UI 顯示錯亂、被裁切，或操作起來很卡時，建議依照這個順序逐步縮小問題範圍。
