# IPC and Protocol

## Transport choices

- MCP client to server: STDIO
- Server to inspector: named pipes

## Why named pipes

Named pipes are a good fit for local Windows-only IPC because they are fast, ACL-aware, and straightforward to secure for a single workstation scenario.

## Message framing

The inspector protocol uses length-prefix framing over named pipes.

```text
[4-byte length][UTF-8 JSON payload]
```

This avoids ambiguity around message boundaries and handles large payloads more safely than delimiter-based framing.

## Request model

- request/response with correlation IDs
- event push from inspector to server when needed
- bounded message sizes and timeouts

## Operational implications

- pipe names are derived from the target process ID
- clients should not assume multiple concurrent servers can share the same target session
- readiness matters as much as process injection; a loaded bootstrapper is not the same as a ready pipe
