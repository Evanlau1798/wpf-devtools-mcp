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

- Unit tests: 3301 currently discovered across the main unit and release-unit suites (`2978 + 323`) via `dotnet test --no-build --list-tests`
- Integration tests: 315 currently discovered in the integration suite via `dotnet test --no-build --list-tests`
- Verified combined total: 3616 currently discovered tests across unit, release-unit, and integration suites

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

## Windows Sandbox local preflight

Use the Windows Sandbox harness before spending hosted CI minutes on release or native verification changes. This is a local preflight, not a GitHub Actions parity guarantee. The sandbox runner maps the repository read-only, writes disposable state under `tmp/sandbox-ci`, and runs CI-shaped PowerShell entrypoints that mirror the important command groups while the hosted workflow remains the final source of truth.

Recommended push gate that closely matches the hosted Windows x64 managed lane:

```powershell
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode HostedWindowsX64 -ReleaseUnitShardCount 8 -UnitDebugShardCount 4 -MaxParallelLanes 4
```

Faster native smoke when the change does not need the full hosted Debug/Release matrix:

```powershell
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode NativeSmoke -ReleaseUnitShardCount 8 -UnitDebugShardCount 4 -MaxParallelLanes 4
```

Useful faster slices:

```powershell
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode UnitDebug -UnitDebugShardCount 4 -MaxParallelLanes 4
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode UnitRelease -ReleaseUnitShardCount 8 -MaxParallelLanes 4
.\scripts\ci\Invoke-WindowsSandboxCi.ps1 -Mode FullManaged -ReleaseUnitShardCount 8 -UnitDebugShardCount 4 -MaxParallelLanes 4
```

Artifact-only local package preflight:

```powershell
.\scripts\tools\packaging\Publish-Release.ps1 -Configuration Debug -Architectures x64 -OutputRoot .\tmp\sandbox-ci\artifact-preflight\release
$package = Get-ChildItem .\tmp\sandbox-ci\artifact-preflight\release -Filter 'release_*_win-x64.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
.\scripts\ci\Invoke-WindowsSandboxArtifactPreflight.ps1 -PackageArchivePath $package.FullName -Architecture x64 -Client other
```

The Debug package example is an unsigned local package smoke. Use artifact preflight when installer, package layout, or packaged server startup behavior changed. This path does not rebuild the repository inside Windows Sandbox. It maps only the release archive and a small preflight bootstrap folder, expands the package, runs the package-local installer, starts the installed MCP server over STDIO, verifies `initialize`, `tools/list`, `resources/read`, and `get_processes`, then uninstalls the package. It does not prove signed Release gate behavior unless you pass a signed Release archive. It also does not launch through a generated client registration entry; registration metadata checks remain covered by the installer/client registration tests.

The artifact preflight provisions .NET runtime channel `8.0` inside the Sandbox when needed, mirroring the hosted runner prerequisite normally supplied by `setup-dotnet`. Use `-DotNetChannel` when validating a different runtime channel, or `-SkipDotNetProvisioning` only when the Sandbox image already has the required runtime.

Operational notes:

- `HostedWindowsX64` mirrors the GitLab Windows x64 fallback lane and the GitHub hosted x64 managed test scope where Windows Sandbox can do so reliably: sandbox-safe native compiler/resource/archive smoke, solution build for Debug/Release, unit shards for both configurations, and release-unit shards for both configurations. It does not cover x86, ARM64, release packaging smoke, coverage, or NuGet pack lanes.
- Windows Sandbox is not reliable for the native DLL link step because Visual C++ linker/resource conversion paths can fail inside the sandbox. Validate the exact `.vcxproj` native DLL link and live integration tests in the normal desktop build environment before pushing native bootstrapper changes.
- `NativeSmoke` validates native compile/resource/archive coverage, then runs managed debug and release unit shards. It intentionally skips the sandbox-specific native DLL link path that is less reliable under Windows Sandbox.
- The optional `-SmokeTargetPath` artifact preflight path currently covers packaged `connect` and scene summary startup smoke only. Use the normal integration/E2E suites for snapshot, mutation, diff, restore, and cleanup workflow coverage.
- The launcher applies host-side scheduling tuning to Windows Sandbox processes by default: `AboveNormal` priority plus disabled execution-speed power throttling. On Intel hybrid CPU systems this helps keep sandbox CI work from being treated as low-QoS E-core-only work. Use `-SkipSandboxHostScheduling` to disable this behavior, or `-SandboxHostProcessorAffinityHex 0x...` only when you intentionally want a machine-specific affinity mask.
- Results and logs are written under `tmp/sandbox-ci/output`; generated `.wsb` files and mapped work state are disposable.
- Use `-GenerateOnly` when reviewing the generated sandbox configuration without launching Windows Sandbox.
- Do not use `taskkill` as the primary cleanup mechanism for Windows Sandbox. Use the tracked `.\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot .\tmp\sandbox-ci\output` script so cleanup targets Windows Sandbox HCS compute systems explicitly. If an existing local worktree already has `tmp\sandbox-ci\Kill-WindowsSandboxHcs.ps1`, that ignored helper can be used for the same purpose, but it is not a tracked source artifact.
- If the machine was freshly booted and Windows Sandbox has not been launched yet, treat any unrelated HCS objects as out of scope. Inspect with `-WhatIf` first before removing candidates.

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
