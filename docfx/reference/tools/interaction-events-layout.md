# Interaction, Event, Layout, and Performance Tools

## Interaction

- `click_element`
- `drag_and_drop`
- `scroll_to_element`
- `simulate_keyboard`
- `element_screenshot`
- `get_focus_state`
- `focus_element`

`get_focus_state` and `focus_element` matter whenever keyboard input, default buttons, tab navigation, or multiple windows are involved.

For `focus_element` and `simulate_keyboard`, pick a visible, enabled, focusable control from the active rendered visual tree. If one real-project target returns `ElementNotLoaded` or cannot receive keyboard focus, use `get_interaction_readiness` or `get_element_snapshot(elementId)` after a concrete elementId is known and retry another loaded focusable candidate before calling the workflow limited.

`element_screenshot` defaults to `outputMode: "metadata"` and also supports `"file"` or `"base64"`. Metadata responses include dimensions, `format`, `rendered: false`, `byteLength: 0`, and a `nextSteps` entry that repeats the call with `outputMode: "file"` when pixel evidence is required, without rendering PNG bytes; metadata mode does not return `screenshotId`, `resourceUri`, or a `wpf://screenshots/{screenshotId}` handle. File and base64 responses render pixels, include `rendered: true`, dimensions, `format`, and `byteLength`; file mode returns `screenshotId`, `resourceUri`, an exact `resourceRead` request, `fileName`, `expiresAtUtc`, `localPathRedacted: true`, and `sha256`, while base64 mode returns `base64Image` only for small inline PNG payloads. Use file mode for larger captures so clients receive a session-scoped resource handle instead of inline pixels. Do not pass `outputPath`; file mode is resource-backed, so call `resourceRead.method` with `resourceRead.uri` in the same MCP server session. Validation agents that need to prove screenshot resource lifecycle behavior should use `outputMode: "file"` and then `resources/read`; metadata mode is intentionally a non-rendering shape/availability probe. File mode is an MCP server-owned retained screenshot resource: the server supplies a per-process server-issued lease root, and `SessionManager` expires it after 24 hours, caps it at 100 resources per MCP server session, deletes retained PNG files when evicted or expired, and purges them when the target session disconnects or the server session manager is disposed. This lifecycle is managed by `SessionManager`, not by the Inspector default screenshot cache.

## State snapshot and sequential mutations

- `capture_state_snapshot`
- `batch_mutate`
- `restore_state_snapshot`

These tools are registered under the State/Mutation category (see `src/WpfDevTools.Mcp.Server/McpTools/StateMcpTools.cs` and `MutationBatchMcpTools.cs`). They are listed together with interaction here because they are the preferred guard rails around destructive UI interactions.

`capture_state_snapshot` and `restore_state_snapshot` are the preferred guard rails before trying UI mutations that may need rollback.

Mutation success responses may include `restoreRequired: true`, `restoreStatus: "notRestored"`, and `restoreSuggestedAction`. These fields mean the tool changed runtime state and the server has not restored it for you. If the app must be left unchanged, use `get_state_diff` when a snapshot is active, then call `restore_state_snapshot` after verification.

Use `batch_mutate` when you need an ordered sequence of live mutations inside one tool call. It is safer than improvising multiple destructive calls in a single agent turn because the server validates and executes the operations sequentially.

Interaction tool responses now also carry `nextSteps` and `navigation`. When the tool already recommends the follow-up, prefer that guidance over a fixed manual verification checklist.

## Routed events

- `trace_routed_events`
- `get_event_handlers`
- `fire_routed_event`
- `drain_events`

`fire_routed_event` is useful for route analysis. It is not a universal substitute for real user input.

If you start a trace session with `trace_routed_events(mode: "start")` before the interaction, the usual next step is `drain_events` to read back the buffered event records explicitly. `trace_routed_events(mode: "get")` remains available for trace-session retrieval, but `drain_events` is the preferred shared-buffer read path when the session may also contain binding, dependency property, or validation events.

Use `maxEvents` on `trace_routed_events(mode: "get")` or capture-mode retrieval when you need to cap trace payload size. Trace responses include `returnedEventCount`, `totalEventCount`, `eventsTruncated`, and `maxEvents` so agents can detect that the `events` array is intentionally partial and retry with a larger cap only when needed.

Trace responses also surface cleanup state when trace teardown is delayed or recovered. Use `cleanupState`, `cleanupFailed`, and `cleanupIncomplete` together: `deferredCompleted` means an earlier cleanup problem recovered and the handlers were removed, while `deferredPending`, `deferredFailed`, or `failed` need more caution before starting another trace.

Some interaction and diagnostic responses may piggyback a compact `pendingEvents` array when buffered events are already available. Use `drain_events` when you need the complete explicit event read step instead of opportunistic piggyback data.

## Layout

- `get_layout_info`
- `highlight_element`
- `get_clipping_info`
- `invalidate_layout`

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

Render statistics bound the internal visual-count walk to 1000 nodes by default. Use `visualCountLimit` and `visualCountTruncated` to tell whether the reported render-stat visual count is complete or intentionally capped.

## Safe usage pattern

1. Inspect first.
2. Call `capture_state_snapshot` before changing UI state.
3. Use `get_focus_state` and `focus_element` before keyboard-sensitive actions.
4. Interact once, or use `batch_mutate` for an ordered mutation sequence.
5. Verify by following `navigation.recommended` or `nextSteps` from the interaction result.
6. If the session has an active snapshot, `get_state_diff` is usually the first follow-up.
7. If the session has buffered runtime events, `drain_events` is usually the first explicit follow-up.
8. Use `restore_state_snapshot` if the workflow requires rollback or if you need to leave the app unchanged.
9. Avoid stacking many independent mutations into one agent step unless `batch_mutate` is the intentional orchestration tool.
