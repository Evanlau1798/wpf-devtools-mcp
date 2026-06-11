# ADR-003: Length-Prefix Framing

## Status

Accepted.

## Context

Named pipes in byte mode do not preserve message boundaries on their own.

## Decision

Prefix every JSON payload with a 4-byte length value.

## Why this was chosen

- minimal overhead
- clear message boundaries
- simple partial-read handling
- works well for variable-size diagnostic payloads

## Consequences

- readers must validate message size
- readers must loop until the full payload is received

For more detail, see [IPC and Protocol](../ipc.md).
