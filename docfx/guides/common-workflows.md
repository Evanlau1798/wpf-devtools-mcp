# Common Workflows

These workflows are stable human-facing baselines, not a replacement for the `navigation` or `nextSteps` hints returned by the tools themselves. When a tool response already recommends the next action, follow that response first and use these workflows as supporting context.

## Diagnose binding failures

1. `connect`
2. `get_binding_errors`
3. Use `navigation.recommended` or `nextSteps` to choose the next action
4. Common follow-ups are `get_bindings`, `get_element_snapshot`, and `get_binding_value_chain`
5. Use `get_datacontext_chain` when the source path is still unclear
6. `force_binding_update` if you need to retrigger evaluation

Use this workflow when the UI looks wrong but the underlying issue may be a stale binding path, missing `DataContext`, converter failure, or invalid source chain. If `get_binding_errors` or `get_bindings` already points to a precise follow-up, prefer that guidance over mechanically running the entire sequence.

## Inspect a visual subtree

1. `get_visual_tree`
2. `get_logical_tree`
3. `get_namescope`
4. `get_template_tree`
5. `compare_trees`

Use this when template-generated elements or content presenters make the logical view diverge from the visual view.

## Analyze dependency property precedence

1. `get_dp_value_source`
2. `get_dp_metadata`
3. `get_applied_styles`
4. `get_resource_chain`
5. `get_triggers`

Use this when a property value is not coming from the source you expected.

## Safe interaction validation

1. Use `get_ui_summary`, `get_element_snapshot`, or `get_interaction_readiness` to confirm the scene and the target
2. Add tree, binding, or command inspection only when more detail is needed
3. Use `click_element`, `simulate_keyboard`, or `drag_and_drop`
4. Follow `navigation.recommended` or `nextSteps` from the interaction result
5. If the current session has an active snapshot, `get_state_diff` is usually the first verification step
6. Without a snapshot, use `get_interaction_readiness`, `get_element_snapshot`, `get_dp_value_source`, or scoped `get_ui_summary` to verify the effect

## Mutation with snapshot rollback

1. `capture_state_snapshot`
2. Inspect the target with scene-level tools or another diagnostic tool
3. Apply one mutation such as `set_dp_value`, `modify_viewmodel`, or `override_style_setter`
4. Call `get_state_diff` first
5. If the tool response offers a more specific `navigation.recommended`, use it for supplementary verification
6. `restore_state_snapshot` if the app should return to its original state

Use this workflow for production-safe debugging, demos, or test sessions where the app must be left unchanged after the experiment. As long as the snapshot is still active, `get_state_diff` should be the default first verification tool after a mutation.

## Focus-sensitive multi-window workflow

1. `get_windows`
2. `get_focus_state`
3. `get_visual_tree` on the active window or target window
4. `focus_element` when the intended control does not currently own focus
5. `simulate_keyboard` or another focus-dependent interaction
6. Verify focus and side effects with `get_focus_state` plus a diagnostic tool

Use this when keyboard shortcuts, Enter/Tab handling, default buttons, or dialog focus ownership may change the outcome.

## Layout and performance triage

1. `get_layout_info`
2. `get_clipping_info`
3. `invalidate_layout`
4. `get_visual_count`
5. `measure_element_render_time`
6. `get_render_stats`
7. `find_binding_leaks`

Use this when the UI is visually broken, clipped, or sluggish.
