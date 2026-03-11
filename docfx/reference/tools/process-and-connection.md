# Process and Connection Tools

## Most important tools

- `get_processes`
- `select_active_process`
- `get_active_process`
- `connect`
- `ping`

## When to use which

- Use `get_processes` first to discover valid WPF targets, architecture, and elevation constraints.
- Use `connect` to create a live inspector session for a specific process.
- Use `select_active_process` after connecting multiple targets when later calls should omit `processId`.
- Use `get_active_process` before a process-id-omission workflow to confirm the active selection.
- Use `ping` after `connect` or before expensive inspection steps to confirm the inspector is still responsive.

## Important behavior

- `get_processes` reports `isElevated`, `requiresElevationToConnect`, and `canConnectFromCurrentServer`
- `connect` validates the target, resolves bootstrapper candidates, and blocks early when the current server lacks permission to attach
- `select_active_process` only succeeds for already-connected sessions
- `get_active_process` exposes whether an active selection exists and when it was chosen
- `ping` is a fast liveness check, not a substitute for `connect`

## Practical sequences

```text
get_processes -> connect -> ping
```

```text
get_processes -> connect -> select_active_process -> get_active_process -> get_visual_tree
```

If these sequences fail, fix the process/session problem before using any process-specific inspection tool.
