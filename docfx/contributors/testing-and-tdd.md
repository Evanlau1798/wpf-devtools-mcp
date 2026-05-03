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

The current project-level verification status combines the latest completed suite baselines with subsequent focused reruns when test counts or scheduling change.

### Test results

- Unit tests: 3135 total, 3135 discoverable via `dotnet test --no-build --list-tests` in the latest count refresh
- Integration tests: 301 total, 301 discoverable via `dotnet test --no-build --list-tests` in the latest count refresh
- Verified combined total: 3436 tests discoverable across unit and integration suites in the latest count refresh

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

## For test parallelization changes

The unit and integration suites enable collection-level parallelization with CPU-scaled worker counts. Keep serial collection lanes narrow and named after the shared state they protect:

- use `InstallerScripts` for installer PowerShell, TUI, process-lifecycle, and package-root tests that need to serialize with each other while still running beside unrelated collections
- use `TimingSensitive` only for timing-budget tests that become unreliable under unrelated workstation contention
- keep the `LiveBootstrapIntegration` collection ordered first because live DLL injection/connect smoke tests are most reliable before the shared testhost accumulates long-running WPF and MCP fixture state
- avoid setting `DisableParallelization = true` unless a collection must not run beside any other collection
- avoid moving unrelated slow tests into a broad serial lane when a smaller collection can preserve isolation and still allow other lanes to run concurrently

## For installer and client registration changes

Installer validation must cover both the registration metadata and the runnable MCP server contract:

1. confirm generated artifacts match the target client schema: VS Code and Visual Studio use `servers`; Cursor, Claude Desktop, and generic MCP clients use `mcpServers`; Claude Code and Codex artifacts use their documented CLI commands
2. confirm each generated `command` value is absolute and points at the installed `wpf-devtools-<arch>.exe`
3. start the installed executable from a registration entry over STDIO and verify the MCP `initialize` plus `tools/list` flow succeeds

This prevents regressions where an installer writes plausible configuration but the installed package cannot actually be started by an MCP client.

## What a good regression test looks like

- fails before the fix
- passes after the minimal fix
- protects the real behavior contract, not only a mock or placeholder branch
