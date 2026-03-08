# Process and Connection Tools

## `get_processes`

Use this first. It discovers candidate WPF processes and reports architecture, which is essential for choosing the right bootstrapper build.

## `connect`

Creates a session for a specific process.

### Important behavior

- validates the process
- resolves inspector and bootstrapper candidates
- enforces architecture compatibility
- performs bootstrap + pipe readiness checks
- creates the session only after readiness succeeds

## `ping`

Use `ping` after `connect` and any time you want a fast liveness check for the active session.

## Practical sequence

```text
get_processes -> connect -> ping
```

If this sequence fails, solve it before using any process-specific tool.