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

### Test discovery baseline

README intentionally avoids exact test-count badges unless they are generated from current discovery output. Use `dotnet test --no-build --list-tests` to discover the current exact counts from the built test assemblies instead of copying a snapshot into this page:

```powershell
dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --no-build --list-tests
dotnet test tests/WpfDevTools.Tests.Unit.Release/WpfDevTools.Tests.Unit.Release.csproj --no-build --list-tests
dotnet test tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj --no-build --list-tests
```

Treat the unit, release-unit, and integration suites as the combined public baseline. Do not update this page with exact test counts; record exact command output only in release notes, PR validation logs, or the relevant audit evidence for that run.

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

## No-VM hosted CI entrypoints

Use the `scripts/tests` hosted CI wrappers when Windows Sandbox is unavailable, too slow for the current iteration, or blocked by local VM state. These scripts do not launch Windows Sandbox. They reuse `scripts/ci/Invoke-HostedCi.ps1` directly on the host and keep disposable state under `tmp/hosted-ci*` by default.

Fast pre-push gate:

```powershell
.\scripts\tests\Invoke-HostedWindowsX64FastCi.ps1
```

`Invoke-HostedWindowsX64FastCi.ps1` runs `HostedWindowsX64Fast`: restore, security-scan equivalence, Debug x64 native bootstrapper build, Debug x64 solution build, Debug unit tests, Release unit shards, Debug server runtime output, and Debug integration tests. It intentionally skips coverage, release packaging smoke, NuGet package smoke, ARM64 cross-build, and DocFX Pages build steps so it can provide faster feedback before a push.

Full no-VM hosted shape:

```powershell
.\scripts\tests\Invoke-HostedWindowsX64Ci.ps1
```

`Invoke-HostedWindowsX64Ci.ps1` runs `HostedWindowsX64`: the fuller hosted Windows x64 shape, including coverage, x64/x86/arm64 package smoke coverage where the x64 host can execute it, NuGet pack and package consumer smoke, ARM64 cross-build, and the local DocFX Pages build. It is closer to GitHub CI/CD than the fast gate, but it still does not emulate GitHub artifact upload/download boundaries, Pages deployment, signed public release publication, or hosted runner image drift.

Both wrappers default to `-MaxParallelLanes 8`, `-ReleaseUnitShardCount 8`, and `-UnitDebugShardCount 1`. The hosted managed lane implementation still caps process-heavy managed concurrency internally where needed for stability.

## Windows Sandbox local preflight

Use the Windows Sandbox harness before spending hosted CI minutes on release or native verification changes. This is a local preflight, not a GitHub Actions parity guarantee. The sandbox runner maps the repository read-only, writes disposable state under `tmp/sandbox-ci`, and runs CI-shaped PowerShell entrypoints that mirror the important command groups while the hosted workflow remains the final source of truth.

Recommended push gate for the hosted Windows CI/CD shape:

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

The Debug package example is an unsigned local package smoke. Use artifact preflight when installer, package layout, or packaged server startup behavior changed. This path does not rebuild the repository inside Windows Sandbox. It maps only the release archive and a small preflight bootstrap folder, expands the package, runs the package-local installer, starts the installed MCP server over STDIO, verifies `initialize`, `tools/list`, `resources/read`, and `get_processes`, then uninstalls the package. The package runtime smoke performs a first run, a second run, a corrupt transport state recovery run, package-local uninstall, reinstall, full-uninstall, and residue validation. It does not prove signed Release gate behavior unless you pass a signed Release archive. It also does not launch through a generated client registration entry; registration metadata checks remain covered by the installer/client registration tests.

The artifact preflight provisions .NET runtime channel `8.0` inside the Sandbox when needed, mirroring the hosted runner prerequisite normally supplied by `setup-dotnet`. Use `-DotNetChannel` when validating a different runtime channel, or `-SkipDotNetProvisioning` only when the Sandbox image already has the required runtime.

Operational notes:

- `HostedWindowsX64` mirrors the GitLab Windows x64 fallback lane and the GitHub hosted Windows CI/CD scope where Windows Sandbox can do so reliably: exact x64 and x86 native bootstrapper builds, Debug/Release x64 and x86 solution builds, x64 unit shards, x64 release-unit shards, and x64 Debug integration tests. It covers coverage, x64/x86/arm64 release packaging smoke, NuGet pack, ARM64 Release cross-build, and the local DocFX Pages build steps. It still does not cover x86 test execution or self-hosted ARM64 runtime smoke lanes. It also does not model GitHub Pages upload/deployment, GitHub artifact upload/download boundaries, signed public release publication, or GitHub-hosted runner image differences.
- The x86 and arm64 packaging smoke follows GitHub's hosted x64 behavior: package-local and online installer install/uninstall layout checks run, but non-x64 packaged server runtime launch is skipped because the hosted x64 lane cannot execute those runtime smokes. Each install/uninstall pair uses an isolated `APPDATA` and `LOCALAPPDATA` root so the package-local and online installer states cannot mask each other.
- `NativeSmoke` validates native compile/resource/archive coverage, then runs managed debug and release unit shards. It intentionally skips the sandbox-specific native DLL link path that is less reliable under Windows Sandbox.
- The optional `-SmokeTargetPath` artifact preflight path currently covers packaged `connect` and scene summary startup smoke only. Use the normal integration/E2E suites for snapshot, mutation, diff, restore, and cleanup workflow coverage.
- The launcher applies host-side scheduling tuning to Windows Sandbox processes by default: `AboveNormal` priority plus disabled execution-speed power throttling. On Intel hybrid CPU systems this helps keep sandbox CI work from being treated as low-QoS E-core-only work. Use `-SkipSandboxHostScheduling` to disable this behavior, or `-SandboxHostProcessorAffinityHex 0x...` only when you intentionally want a machine-specific affinity mask.
- Results and logs are written under `tmp/sandbox-ci/output`; generated `.wsb` files and mapped work state are disposable.
- Use `-GenerateOnly` when reviewing the generated sandbox configuration without launching Windows Sandbox.
- Do not use `taskkill` as the primary cleanup mechanism for Windows Sandbox. Use the tracked `.\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot .\tmp\sandbox-ci\output -WhatIf` script first so cleanup candidates are explicitly limited to Windows Sandbox HCS compute systems. If an existing local worktree already has `tmp\sandbox-ci\Kill-WindowsSandboxHcs.ps1`, that ignored helper can be used for the same purpose, but it is not a tracked source artifact.
- If the machine was freshly booted and Windows Sandbox has not been launched yet, treat any unrelated HCS objects as out of scope. Inspect with `-WhatIf` first before removing candidates; only rerun with `-Force` or explicit `-Confirm:$false` after verifying every candidate is a Windows Sandbox compute system.

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
