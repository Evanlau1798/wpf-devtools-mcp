# Testing and TDD

## Repository expectations

The repository expects a strict red-green-refactor cycle for code changes.

## Core commands

Build and test separately to avoid file-lock issues:

```powershell
dotnet build WpfDevTools.sln -c Debug
dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --no-build
dotnet test tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj --no-build
```

## For MCP workflow changes

When tool semantics or server behavior changes, prefer this verification order:

1. unit tests
2. integration tests
3. live MCP smoke harness against the test app

## What a good regression test looks like

- fails before the fix
- passes after the minimal fix
- protects the real behavior contract, not only a mock or placeholder branch