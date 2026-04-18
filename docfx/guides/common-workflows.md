# Common Workflows

These workflows are stable human-facing baselines, not a replacement for the `navigation` or `nextSteps` hints returned by the tools themselves. When a tool response already recommends the next action, follow that response first and use these workflows as supporting context.

## Diagnose binding failures

1. `connect`
2. `get_binding_errors`
3. Use `navigation.recommended` or `nextSteps` to choose the next action
4. Common follow-ups are `get_affected_elements`, `get_bindings`, `get_element_snapshot`, and `get_binding_value_chain`
5. Use `get_datacontext_chain` when the source path is still unclear
6. `force_binding_update` if you need to retrigger evaluation
7. `drain_events` if you need an explicit read of buffered binding or validation events after a mutation

Use this workflow when the UI looks wrong but the underlying issue may be a stale binding path, missing `DataContext`, converter failure, or invalid source chain. If `get_binding_errors` or `get_bindings` already points to a precise follow-up, prefer that guidance over mechanically running the entire sequence. Keep `get_binding_errors` in compact mode by default, and request verbose output only when the trimmed summary is insufficient.

## Inspect a visual subtree

1. `connect`
2. `get_ui_summary`
3. `find_elements`
4. `get_visual_tree`
5. `get_logical_tree`
6. `get_namescope`
7. `get_template_tree`
8. `compare_trees`

Use this when template-generated elements or content presenters make the logical view diverge from the visual view. Start with the scene summary so you only expand trees when semantic context is not enough.

## Analyze dependency property precedence

1. `connect`
2. `get_dp_value_source`
3. `get_dp_metadata`
4. `get_applied_styles`
5. `get_resource_chain`
6. `get_triggers`

Use this when a property value is not coming from the source you expected.

## Safe interaction validation

1. `connect`
2. Use `get_ui_summary`, `get_element_snapshot`, or `get_interaction_readiness` to confirm the scene and the target
3. Add tree, binding, or command inspection only when more detail is needed
4. Use `click_element`, `simulate_keyboard`, or `drag_and_drop`
5. Follow `navigation.recommended` or `nextSteps` from the interaction result
6. If the current session has an active snapshot, `get_state_diff` is usually the first verification step
7. If the current session has buffered runtime events, `drain_events` is the first explicit event-verification step
8. Without a snapshot, use `get_interaction_readiness`, `get_element_snapshot`, `get_dp_value_source`, or scoped `get_ui_summary` to verify the effect

## Mutation with snapshot rollback

1. `connect`
2. `capture_state_snapshot`
3. Inspect the target with scene-level tools or another diagnostic tool
4. Apply one mutation such as `set_dp_value`, `modify_viewmodel`, or `override_style_setter`, or use `batch_mutate` for an ordered sequence
5. Call `get_state_diff` first
6. Call `drain_events` when you need an explicit read of buffered binding, DP, or validation events
7. If the tool response offers a more specific `navigation.recommended`, use it for supplementary verification
8. `restore_state_snapshot` if the app should return to its original state

Use this workflow for production-safe debugging, demos, or test sessions where the app must be left unchanged after the experiment. As long as the snapshot is still active, `get_state_diff` should be the default first verification tool after a mutation.

## Focus-sensitive multi-window workflow

1. `connect`
2. `get_windows`
3. `get_focus_state`
4. `get_visual_tree` on the active window or target window
5. `focus_element` when the intended control does not currently own focus
6. `simulate_keyboard` or another focus-dependent interaction
7. Verify focus and side effects with `get_focus_state` plus a diagnostic tool

Use this when keyboard shortcuts, Enter/Tab handling, default buttons, or dialog focus ownership may change the outcome.

## Layout and performance triage

1. `connect`
2. `get_layout_info`
3. `get_clipping_info`
4. `invalidate_layout`
5. `get_visual_count`
6. `measure_element_render_time`
7. `get_render_stats`
8. `find_binding_leaks`

Use this when the UI is visually broken, clipped, or sluggish.
