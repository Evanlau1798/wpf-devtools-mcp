# Injection and Runtime Selection

## Runtime selection problem

WPF applications may run on .NET Framework or modern .NET. The server must select the correct inspector target framework at runtime.

## Current approach

- detect the target runtime family
- detect the target process architecture
- select the correct inspector payload
- inject the native bootstrapper
- host the managed bridge for that runtime
- wait for the named pipe readiness signal

## Why not direct inspector injection

The bootstrapper model improves startup diagnostics and makes the connect success contract more trustworthy. It also centralizes runtime-specific hosting logic.

## Important rule for operators

Architecture matching is not optional. The injector/bootstrapper path requires a build that matches the target process bitness.

## Important rule for contributors

Treat `connect` as a staged pipeline, not a single operation. Error handling, tests, and docs should preserve the distinction between:

- validation failure
- bootstrap failure
- readiness timeout
- session success