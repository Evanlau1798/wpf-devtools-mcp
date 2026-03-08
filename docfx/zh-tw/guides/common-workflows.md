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

## Layout 與效能 triage

1. `get_layout_info`
2. `get_clipping_info`
3. `invalidate_layout`
4. `get_visual_count`
5. `measure_element_render_time`
6. `get_render_stats`
7. `find_binding_leaks`

當 UI 顯示錯亂、被裁切，或操作起來很卡時，建議依照這個順序逐步縮小問題範圍。
