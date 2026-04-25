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

## Current validation snapshot

The current project-level verification status is based on the latest completed full-suite run.

### Test results

- Unit tests: 2767 total, 2767 passed, 0 failed
- Integration tests: 289 total, 289 passed, 0 failed
- Full-suite total: 3056 tests, 3056 passed, 0 failed

### Coverage

- Last merged coverage snapshot: 83.4% line, 71.8% branch, 94.2% method
- Coverage source: merged unit and integration Cobertura reports generated with `coverlet.runsettings`; coverage was not rerun during the latest full-suite validation
- The coverage report still contains testable `WpfDevTools.Injector` discovery and helper code
- Injection-only runtime entry points remain excluded via `[ExcludeFromCodeCoverage]`

### Current red slices

- No red slices remain in the latest unit and integration full-suite validation.
- The previous installer integrity, named-pipe compatibility, ping/replay, structured fallback, FileLogger shutdown, and `wait_for_dp_change_after_mutation` slices are now covered by passing tests.

## For MCP workflow changes

When tool semantics or server behavior changes, prefer this verification order:

1. unit tests
2. integration tests
3. live MCP smoke harness against the test app

## What a good regression test looks like

- fails before the fix
- passes after the minimal fix
- protects the real behavior contract, not only a mock or placeholder branch
