# ADR Index

These Architecture Decision Records explain the decisions that most affect users, operators, and contributors.

## Included ADRs

- [ADR-001: Named Pipes for IPC](adr-001-named-pipes.md)
- [ADR-002: In-Process Injection](adr-002-in-process-injection.md)
- [ADR-003: Length-Prefix Framing](adr-003-length-prefix-framing.md)
- [ADR-005: Multi-Targeting Strategy](adr-005-multi-targeting.md)
- [ADR-006: STDIO Session State](adr-006-stdio-session-state.md)

## How to read them

- Start with [Architecture Overview](../overview.md) if you are new to the project.
- Read ADR-001 and ADR-002 to understand the two most fundamental design choices.
- Read ADR-005 before changing bootstrapper, packaging, or compatibility behavior.
- Read ADR-006 before adding Streamable HTTP, SSE, or any multi-client server transport.
