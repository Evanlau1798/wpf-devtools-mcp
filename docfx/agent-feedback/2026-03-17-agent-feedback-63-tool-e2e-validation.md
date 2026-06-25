# Agent Feedback: 63-Tool E2E Validation

## Context

- Agent: Claude Opus 4.6 (Claude Code CLI)
- Date: 2026-03-17
- App / scenario: Custom edge-case WPF app (tmp/EdgeCaseApp) with binding errors, visibility edge cases, MultiBinding, DataContext switching, clipping, multi-window, disabled commands, and mutable ViewModel state
- Build / release version: Local dev build (master branch)

## Workflow tested

1. Connection and process discovery (connect, get_processes, get_active_process, select_active_process, ping)
2. Scene-first investigation (get_ui_summary, get_form_summary, diagnose_visibility, get_interaction_readiness, get_element_snapshot after an elementId is known)
3. Full 63-tool validation covering all 10 tool categories with cross-tool workflows (binding error triage, snapshot/diff/restore, batch_mutate, expression rollback, routed event tracing, focus-sensitive keyboard interaction)

## What worked well

- **Scene-first tools eliminated screenshot dependency entirely.** `get_ui_summary(semantic)` returned 82 annotated nodes with `[visibility:Hidden]`, `[disabled]`, `[transparent]` inline — I never needed a screenshot to understand the app state.
- **Navigation hints (`navigation.recommended`) consistently suggested the correct next tool** with pre-filled parameters. After `get_binding_errors`, it recommended `get_datacontext_chain` for the exact failing elements. After `capture_state_snapshot`, it recommended `get_state_diff` with the correct snapshotId. This reduced my planning overhead to near-zero.
- **Piggybacked events on mutation/interaction responses** saved round-trips. `click_element(SaveButton)` included both the DpChange on StatusText and the RoutedEvent in the response, making it unnecessary to call `drain_events` separately.
- **Expression-backed DP rollback worked flawlessly.** `set_dp_value` on a binding-backed TextBox.Text reported `replacedExpression=true, capturedRollbackExpression=true`. `clear_dp_value` then restored the binding with `restoredExpression=true, expressionKind=Binding`. This is a critical safety feature.
- **Snapshot/diff/restore cycle was deterministic.** Captured 1 DP + 2 VM properties + focus. After 2 mutations and a command execution, `get_state_diff` detected all changes. `restore_state_snapshot` restored everything with zero skipped properties and zero warnings.
- **`batch_mutate` with `includeDiff=true`** executed 2 sequential mutations and returned an integrated diff in one call — a significant token saver for multi-step workflows.
- **`get_binding_mismatches(recursive=true)`** correctly distinguished PathMismatch (severity=Warning) from TypeMismatchWithConverter (severity=Info), showing awareness that a converter mediates the type difference.
- **`get_affected_elements(propertyName=FirstName)`** detected 4 affected elements with `matchStrategy=multibinding-child-path-match` and high confidence — it found the MultiBinding child path on FullNameDisplay, consistent with the full value chain trace.
- **`get_binding_value_chain` fully traces MultiBinding.** For a TextBlock with `MultiBinding` + `FullNameConverter`, the chain returned 7 steps: MultiBinding definition with converter, BindingInput[0] (FirstName="Alice", Resolved), BindingInput[1] (LastName="Smith", Resolved), DataContext chain, ResolvedSource, and FinalValue="Alice Smith". This gives agents complete visibility into multi-input value resolution.
- **`diagnose_visibility`** correctly diagnosed three different root causes (Visibility=Hidden, Visibility=Collapsed with zero layout size, Opacity=0) and provided actionable `suggestedFix` values.
- **`get_form_summary`** identified BrokenButton blockers (`ElementDisabled` + `CommandCannotExecute`) and FocusTestButton blocker (`ElementInInactiveTab`) — exactly what an agent needs before attempting interaction.
- **`wait_for_dp_change` with `triggerMutation`** is brilliant for serialized STDIO clients. It executed `modify_viewmodel(StatusMessage=WaitTest)` and immediately detected the value change with `elapsedMs=0`.
- **`trace_routed_events` start+get workflow captured events reliably** with a 5s window using `allowShortStartDuration=true`. The start→click_element→get sequence correctly captured the Click bubble event.
- **`get_render_stats(warmUp=true)`** returned high-confidence data (30 samples, frameRate=35.31, avgRenderTime=28.32ms) in a single call, eliminating the need for multi-call warm-up patterns.
- **Token efficiency controls are well-designed.** `compact=true`, `summaryOnly=true`, `navigation=false`, `maxNodes`, `maxChildrenPerNode` all work as documented and meaningfully reduce response size.

## Friction observed

- **`get_form_summary` label heuristic for inputs without explicit Label/Target bindings** assigns the nearest heading as the label. FocusBox1, FocusBox2, FocusBox3 all got label="Focus and Keyboard Testing" (the section heading), which is ambiguous when multiple inputs share the same inferred label.
- **`drag_and_drop` requires application-side drop handlers** to produce visible effects. The tool correctly simulated the drag-drop event sequence (dropped=true), but without a handler in the target app there is no observable state change. This is expected behavior, but agents should be aware that a successful response does not guarantee the app handled the drop.

## Concrete examples

```text
Tool call: get_binding_value_chain(elementId=TextBlock_1, propertyName=Text)
Result: {
  hasBinding: true,
  chainLength: 7,
  chain: [
    { step: "Binding", bindingType: "MultiBinding", converter: "FullNameConverter", bindingPaths: ["FirstName","LastName"] },
    { step: "BindingInput", bindingIndex: 0, path: "FirstName", value: "Alice", resolutionState: "Resolved" },
    { step: "BindingInput", bindingIndex: 1, path: "LastName", value: "Smith", resolutionState: "Resolved" },
    { step: "LocalDataContext", dataContextType: "MainViewModel" },
    { step: "InheritedDataContext", dataContextType: "MainViewModel" },
    { step: "ResolvedSource", sourceType: "MultiBinding", resolutionState: "Resolved" },
    { step: "FinalValue", value: "Alice Smith", valueType: "String" }
  ]
}
Assessment: Complete MultiBinding value resolution visible in one call.
```

```text
Tool call: trace_routed_events(mode=start, eventName=Click, durationMs=5000, allowShortStartDuration=true)
  then: click_element(elementId=Button_2)
  then: trace_routed_events(mode=get)
Result: { eventCount: 1, events: [{ sender: "Button", senderName: "ResetButton", eventName: "Click", routingStrategy: "Bubble" }] }
Assessment: 5s window with allowShortStartDuration=true is sufficient for agent IPC round-trips.
```

```text
Tool call: get_render_stats(warmUp=true)
Result: { isWarmedUp: true, confidence: "high", sampleCount: 30, frameRate: 35.31, avgRenderTime: 28.32 }
Assessment: Single-call warm-up produces high-confidence render metrics without multi-call patterns.
```

## Suggested improvements

- For `get_form_summary`, when the label heuristic assigns a section heading to multiple inputs, consider appending the input's x:Name or index to disambiguate (e.g., "Focus and Keyboard Testing > FocusBox1")
- For `drag_and_drop`, consider reporting whether the target element has any registered Drop/DragOver handlers so agents can anticipate whether the drop will be handled

## Priority assessment

- P0: (none — all 63 tools work correctly)
- P1: (none)
- P2: Form label disambiguation for inputs sharing a common section heading; drag_and_drop handler presence hint

## Token / payload observations

- `get_ui_summary(semantic, depth=3)` for 82 nodes: well-structured but large response. `summaryOnly=true` significantly reduces payload when only summaryText is needed.
- `navigation=false` confirmed to strip both `navigation` and `nextSteps` — measurable token savings for agents that already know their next step.
- `batch_mutate` with `includeDiff=true` returns a combined response that would otherwise require 4 separate calls (capture + 2 mutations + diff). Significant token efficiency gain.
- Piggybacked events on mutation responses (`pendingEvents`) add modest payload but save an entire `drain_events` round-trip.
- `compact=true` on tree tools and `compact=true` on `get_dp_value_source` meaningfully reduce per-call tokens for repeated diagnostic patterns.
- `get_binding_value_chain` on MultiBinding returns a 7-step chain — richer than simple Binding chains but well-structured with per-input `bindingIndex` correlation.

## Additional notes

- The overall experience rating is **10/10**. Every tool works correctly, including full MultiBinding value chain tracing, reliable routed event capture with short windows, and single-call warm-up for performance metrics.
- The response contract (v2026-03-13-ai-friendly-v3) is well-designed. The `navigation.recommended` vs `nextSteps` layering lets advanced agents use the richer envelope while maintaining backward compatibility.
- The `contextRefs` entries (e.g., `type=binding-issue`, `type=mutation-session`) are useful for agents maintaining conversation state across tool calls.
- Auto-discovery via `connect()` with no parameters is the correct default workflow. The fallback to `get_processes` for disambiguation is clean.
- The `depthSufficiencyHint` on tree tools (with `reasonCode`, `recommendedDepth`, and `suggestion`) is a smart pattern that prevents agents from under-fetching without requiring them to over-fetch on the first call.
- The MultiBinding chain in `get_binding_value_chain` is particularly well-structured — each child binding gets its own `BindingInput` step with `bindingIndex`, making it trivial for agents to correlate inputs with the final converter output.
