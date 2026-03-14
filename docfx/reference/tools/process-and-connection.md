# Process and Connection Tools

## Most important tools

- `get_processes`
- `select_active_process`
- `get_active_process`
- `connect`
- `ping`

## When to use which

- Use `connect()` first for the common case. It auto-discovers a single visible WPF target and connects in one step.
- Use `get_processes(windowFilter)` when auto-discovery is ambiguous, when you need architecture/elevation details up front, or when you want to inspect background targets.
- Use `connect(processId)` to create a live inspector session for a specific process after explicit discovery.
- Use `select_active_process` after connecting multiple targets when later calls should omit `processId`.
- Use `get_active_process` before a process-id-omission workflow to confirm the active selection.
- Use `ping` only when you need an explicit liveness check or reconnect confirmation.

## Important behavior

- `get_processes` reports `isElevated`, `requiresElevationToConnect`, and `canConnectFromCurrentServer`
- `connect()` auto-discovers a single visible WPF target by default and returns deterministic candidate data when multiple targets are found
- `connect` validates the target, resolves bootstrapper candidates, and blocks early when the current server lacks permission to attach
- `select_active_process` only succeeds for already-connected sessions
- `get_active_process` exposes whether an active selection exists and when it was chosen
- `ping` is a fast liveness check, not a substitute for `connect`

## Practical sequences

```text
connect -> get_ui_summary -> get_element_snapshot
```

```text
connect -> get_ui_summary -> find_elements -> get_visual_tree
```

```text
connect -> MultipleWpfProcessesFound -> get_processes(windowFilter) -> connect(processId)
```

Exception-only discovery path:

```text
get_processes(windowFilter) -> connect(processId) -> select_active_process -> get_active_process -> get_ui_summary
```

If these sequences fail, fix the process/session problem before using any process-specific inspection tool.
