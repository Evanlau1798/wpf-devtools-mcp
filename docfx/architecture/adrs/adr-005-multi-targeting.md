# ADR-005: Multi-Targeting Strategy

## Status

Accepted.

## Context

Real-world WPF applications span both .NET Framework and modern .NET, and they may run as x86, x64, or ARM64 processes.

## Decision

Keep the MCP server on modern .NET, multi-target shared/inspector components where required, and select the correct runtime payload dynamically at connect time.

## Why this was chosen

- maximizes compatibility with real WPF applications
- keeps the server on a modern runtime
- makes runtime-specific hosting explicit

## Consequences

- more build outputs and more testing responsibility
- architecture matching must be documented and enforced carefully
- packaging and release artifacts must stay architecture-aware

For more detail, see [Compatibility Matrix](../../production/compatibility-matrix.md).