# Common Workflows

## Diagnose binding failures

1. `connect`
2. `get_binding_errors`
3. `get_bindings`
4. `get_binding_value_chain`
5. `get_datacontext_chain`
6. `force_binding_update` if you need to retrigger evaluation

Use this workflow when the UI looks wrong but the underlying issue may be a stale binding path, missing `DataContext`, converter failure, or invalid source chain.

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

1. Locate the target with `get_visual_tree`
2. Confirm bindings or commands if relevant
3. Use `click_element`, `simulate_keyboard`, or `drag_and_drop`
4. Verify the result with `get_dp_value_source`, `get_viewmodel`, or another inspection tool

## Layout and performance triage

1. `get_layout_info`
2. `get_clipping_info`
3. `invalidate_layout`
4. `get_visual_count`
5. `measure_element_render_time`
6. `get_render_stats`
7. `find_binding_leaks`

Use this when the UI is visually broken, clipped, or sluggish.