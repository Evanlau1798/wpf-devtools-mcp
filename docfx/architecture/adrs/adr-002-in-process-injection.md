# ADR-002: In-Process Injection

## Status

Accepted.

## Context

The project needs access to WPF APIs that are unavailable to out-of-process automation, including binding internals, dependency property analysis, and tree operations that must run on the target Dispatcher.

## Decision

Use in-process injection so the inspector executes inside the target WPF application.

## Why this was chosen

- enables deep WPF-specific inspection
- supports MVVM, binding, and dependency property analysis
- allows precise routed-event and layout operations

## Consequences

- higher complexity than UI Automation
- architecture, privilege, and code-signing concerns matter
- stability and timeout handling must be treated as first-class concerns

For more detail, see [Injection and Runtime Selection](../injection.md).