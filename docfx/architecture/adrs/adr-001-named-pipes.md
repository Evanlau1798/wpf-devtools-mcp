# ADR-001: Named Pipes for IPC

## Status

Accepted.

## Context

The server needs low-latency, local, bidirectional IPC between the MCP host and the injected inspector process.

## Decision

Use Windows named pipes with process-derived pipe names, ACL restrictions, JSON payloads, and explicit framing.

## Why this was chosen

- lower latency than local HTTP for this use case
- Windows-native security model
- natural fit for same-machine communication
- simpler operational surface than sockets or WCF

## Consequences

- Windows-only by design
- strong fit for local developer tooling
- requires explicit framing and timeout handling

For more detail, see [IPC and Protocol](../ipc.md).