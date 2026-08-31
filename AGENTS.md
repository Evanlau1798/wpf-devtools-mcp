# AGENTS.md

This file provides guidance to Codex and other coding agents working in this repository.

## Language And Communication

- Use Traditional Chinese for user-facing conversation.
- Use English for commit messages.
- Do not use emojis in responses, source comments, or commit messages.
- Keep user updates concise and factual.

## Repository Rules

- Follow production-grade rigor for all code changes.
- Follow strict TDD for bug fixes and behavior changes:
  1. RED: write or update a test and verify it fails for the expected reason.
  2. GREEN: implement the smallest change that passes.
  3. REFACTOR: clean up while keeping tests green.
- Keep source and test files under 500 lines. Planning markdown files are exempt.
- Read `.gitignore` before editing it.
- `.gitignore` may only gain new entries; never remove existing ignore rules.
- Do not commit any files under `docs/`.
- `docfx/` files are commit-able when they are part of product documentation.
- One-off logs, local notes, and temporary test artifacts must be deleted or placed under `tmp/`.
- Do not commit generated temporary files.
- Build and test separately to avoid file-lock issues.
- Prefer tool-based file editing and `apply_patch` for manual edits.
- Avoid PowerShell commands that may corrupt Chinese text encoding.
- When the user provides a finding checklist or remediation plan, treat each finding as a separately verifiable unit. Mark the matching checklist item complete only after the fix is verified.
- If the user asks for per-finding commits, commit after each completed and verified finding with an English conventional commit message.

## Current Product Contract

- The server exposes 77 MCP tools.
- `connect()` is the default entry workflow and supports auto-discovery.
- `get_processes(windowFilter)` is for disambiguation or explicit window scoping.
- `get_ui_summary` defaults to `depthMode: "semantic"`.
- Scene-level tools are the primary workflow, not an advanced add-on.
- `wait_for_dp_change` is the polling-friendly STDIO fallback for bounded waits.
- Raw-injection first-run sessions need both `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` and `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` set to the exact local target executable path.
- Scene, tree, binding, DP, and state reads that can expose target UI text or runtime values need `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`.
- Mutating workflows should explicitly enable only the required gates and should use snapshot/diff/restore discipline.

## Default MCP Workflow

1. `connect()`
2. `get_active_process`
3. Run scene, tree, diagnostic, or mutation-safe tools.
4. Use `ping` only when an explicit health check is needed.

## Disambiguation Workflow

1. `get_processes(windowFilter)`
2. `connect(processId)`
3. `select_active_process(processId)` only when explicitly switching away from the active process.

## Scene-First Workflow

1. `get_ui_summary`
2. `get_element_snapshot`
3. `get_form_summary` or `diagnose_visibility`
4. Use atomic tree, binding, DP, or event tools only when deeper inspection is needed.

## Mutation-Safe Workflow

1. `capture_state_snapshot`
2. Perform the mutation or interaction.
3. `get_state_diff`
4. `restore_state_snapshot`

For a minimal valid rollback guard, use `capture_state_snapshot` with at least one of `propertyNames`, `viewModelPropertyNames`, or `includeFocus`. An empty argument object is expected to return `MissingRequiredParameter`.

## Public Endpoint And Release Discipline

- Installer alias: `https://installer.wpf-mcptools.evanlau1798.com/`
- DocFX site: `https://wpf-mcptools.evanlau1798.com/`
- Do not use the installer alias as a DocFX endpoint. It redirects to the reviewed `scripts/online-installer.ps1` entrypoint.
- README and public docs should link documentation to the DocFX site, not directly to local markdown files unless the local file is intentionally repo-only.
- Prefer GitHub pre-release assets for real-user E2E validation before the first stable release. Do not create or promote a stable release unless the user explicitly asks or release criteria require it.
- E2E install tests should use the public online installer plus GitHub release or pre-release assets. Do not use `git clone` as the installation path unless the test is explicitly about source build or package creation.
- Before each public-path E2E run, push the relevant commit to GitHub so the installer and docs reflect the intended state. If the user allows it, E2E may start before hosted CI finishes, but final completion still requires CI/CD and DocFX status to be checked.
- If DNS or endpoint behavior is in scope, verify both the HTTP status and redirect target. Use `cfcli` only when DNS changes are actually required, and report missing API permissions precisely.

## Agent-Driven E2E Discipline

- When the user requests real Agent E2E, the testing agent must read `docs/E2E Validation Task.md` first.
- The testing agent must actually call the installed MCP server over STDIO and inspect JSON-RPC results. Do not infer success from source reflection, build output, or docs alone.
- Keep E2E evidence under `tmp/`, including installer output, MCP transcripts, server stderr, cleanup state, and final reports.
- E2E reports should distinguish P0/P1 product failures from P2/P3 ergonomics, documentation, or harness issues.
- At minimum, public-path E2E should verify installer download, prerelease resolution, `initialize`, `tools/list` count of 77, `connect`, `get_active_process`, `get_ui_summary`, a focused read, snapshot/diff/restore, uninstall, and process cleanup.
- Prefer the golden TestApp for repeatable regression coverage. Temporary edge-case apps under `tmp/` are useful for exploratory Agent feedback, but important scenarios should be promoted into deterministic project tests when practical.
- If a subagent performs E2E or review, capture its report path and close the subagent after it returns to avoid exhausting agent slots.

### E2E evidence and interaction hard gates

- Keep formal E2E at NO-GO until product P1/P2 findings, non-E2E verification, and the evidence validator gates are complete.
- Use manifest schema `wpfdevtools.e2e-run-evidence.v1`. Every artifact path must stay under one run evidence root and carry a verified SHA-256.
- Run `scripts/e2e/Test-E2ERunEvidence.ps1 -Phase PreJudge` before visual judging and `-Phase Final` only after runner, judge, report, and cleanup evidence exist. Both phases are fail-closed.
- Final output is exactly `runnerCompleted`, `operationalGatesPassed`, `visualQualified`, `overallResult`, `reasons`, and `repairBudgetExhausted`. PASS requires every operational, report, cleanup, and strict visual gate.
- The actual launcher must write one complete JSON event per line with `System.Text.Json` and UTF-8 without BOM. Validator fixtures do not close the JSONL finding; validate a real launcher artifact.
- Positive MCP evidence requires `isError=false`, `structuredContent.success=true`, and a proved semantic postcondition. Preview readiness requires every success flag, no truncation, and zero attention-required warnings.
- Build the interactive inventory from the union of core-journey checkpoints. Include every loaded, visible, enabled, hit-testable app-authored control with a unique `x:Name` or Composer correlation.
- Only template-generated parts, OS chrome, hidden, disabled, or non-hit-testable controls may be excluded, and each exclusion needs runtime evidence.
- Buttons, menus, and navigation actions require real `ICommand` and `CommandParameter` bindings. Selectors require collection/`ItemsSource` and selection-to-ViewModel bindings. Text, toggle, slider, date, and value controls require primary-state bindings.
- A `ScrollViewer` may use `native-state-only`, but before/after offsets must prove real interaction. App-authored command controls inside a data template remain independently eligible.
- Execute the core journey: locate scene, select a meaningful item, verify detail and selection ViewModel state, run the primary bound command, verify visible and ViewModel feedback, perform needed secondary interaction, diff, restore, then verify original selection, state, and focus.
- Smoke every remaining eligible control through MCP-native interaction. Do not use Computer Use or OS synthetic input.
- Keep the blind visual judge pixels-only, blind, and isolated. Freeze prepared reference and candidate images into attempt-local `inputs/`, hash them, write `visual-judge-inputs.json`, and pass only frozen paths.
- The canonical reference is the app-only crop from the 1920x1279 source at `x=0,y=0,width=1920,height=1215`. Candidate screenshots use app-window scope, at most 1% aspect-ratio error, and the largest matching size that fits the work area.
- Contract recovery is unlimited before attempt 1 but never enters the judge. After attempt 1, permit at most one aesthetic repair; attempt 2 must reuse the same visual-contract hash. Final validation owns qualification and repair-budget decisions.

## Agent Feedback Triage

- Treat Agent feedback as evidence, not as a patch request. Verify each finding against current source, docs, workflow, or public endpoint state before changing code.
- Preserve useful E2E feedback reports under `tmp/` or ignored `docs/` files unless the user asks for product documentation.
- When consolidating Agent feedback, separate:
  - already fixed items that need regression guards;
  - active P0/P1 blockers;
  - P2/P3 ergonomics or documentation improvements;
  - test harness limitations.
- Recent Agent feedback showed this product is strongest when agents use scene-first inspection, stable element targeting, structured recovery, and mutation-safe snapshot discipline.
- Recurring improvement themes are rate-limit ergonomics, policy profile discoverability, event-buffer prior-context clarity, mutating-tool restore hints, `batch_mutate` schema ergonomics, screenshot metadata wording, and focused-call navigation guidance.

## Architecture

```text
AI Agent
    -> MCP Protocol over STDIO
MCP Server
    -> Named Pipes IPC
Injected Inspector DLL
    -> Direct WPF runtime access
Target WPF Application
```

Core constraints:

- In-process inspection is required for WPF runtime APIs such as `BindingOperations`, `DependencyPropertyHelper`, `VisualTreeHelper`, and MVVM state.
- UI-thread marshalling is mandatory for WPF object access.
- Named Pipes use explicit framing and correlation IDs.
- Structured error contracts are preferred over raw error strings.
- Scene-level aggregation should be tried before screenshot-first or full-tree reasoning.

## Important Tool Areas

- Process management: `get_processes`, `connect`, `select_active_process`, `get_active_process`, `ping`
- Tree and search: `find_elements`, `get_visual_tree`, `get_logical_tree`, `get_template_tree`, `get_namescope`
- Binding and DP: `get_binding_errors`, `get_bindings`, `get_binding_mismatches`, `get_binding_value_chain`, `get_dp_value_source`, `wait_for_dp_change`
- Scene diagnostics: `get_ui_summary`, `get_form_summary`, `get_element_snapshot`, `diagnose_visibility`, `get_interaction_readiness`
- State safety: `capture_state_snapshot`, `get_state_diff`, `restore_state_snapshot`, `batch_mutate`

## Build and Test Commands

Build:

```powershell
dotnet build
dotnet build -c Release
dotnet build -r win-x64
dotnet build -r win-x86
dotnet build -r win-arm64
```

Unit tests:

```powershell
dotnet build tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj
dotnet test tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj --no-build
```

Integration tests:

```powershell
dotnet build tests\WpfDevTools.Tests.Integration\WpfDevTools.Tests.Integration.csproj
dotnet test tests\WpfDevTools.Tests.Integration\WpfDevTools.Tests.Integration.csproj --no-build
```

MCP server:

```powershell
dotnet run --project src\WpfDevTools.Mcp.Server\
```

Test WPF app:

```powershell
dotnet run --project tests\WpfDevTools.Tests.TestApp\
```

DocFX validation:

```powershell
dotnet tool run docfx docfx\docfx.json
powershell -ExecutionPolicy Bypass -File scripts\ci\Test-DocFxDocumentation.ps1 -RepoRoot .
```

Hosted CI wrappers without Windows Sandbox:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\tests\Invoke-HostedCiFast.ps1
powershell -ExecutionPolicy Bypass -File scripts\tests\Invoke-HostedCiFull.ps1
```

## Worktree Discipline

- Use a dedicated git worktree for multi-step or risky feature work.
- Merge back to `master` only after targeted verification, full verification when practical, and review.
- Do not remove unrelated user work.
- Do not use destructive git commands unless explicitly requested.

## Testing Notes

- Prefer `dotnet build ...` followed by `dotnet test ... --no-build`.
- Close running MCP servers, TestApp instances, and injected targets before broad rebuilds.
- In WPF STA tests, avoid `Task.Run(...Dispatcher.Invoke...)` plus blocking waits.
- Prefer direct STA-thread mutation for DependencyProperty tests.
- For asynchronous test-window cleanup, marshal cleanup back to the owning dispatcher.
- If behavior depends on real dispatcher pumping, prefer a WPF integration test over a brittle unit test.
- For installer, release, and package changes, validate the installed executable path, not only source-run behavior.
- For STDIO MCP checks, use newline-delimited JSON-RPC messages. Do not use `Content-Length` framing with this server's STDIO transport.
- If GitHub minutes are limited, use local hosted CI wrappers and explicitly compare any remaining gap against `.github/workflows/*`.
- If Windows Sandbox CI appears stuck, inspect sandbox result files, logs, and relevant processes first. Prefer project cleanup scripts over manual process termination.

## Runtime Environment Notes

- If named-pipe connection timeouts occur only inside sandboxed agent sessions, treat the execution environment as suspect before labeling a product regression.
- Cross-user or sandboxed sessions can distort named pipe behavior.
- DLL injection and bootstrapper flows may trigger security tooling; do not treat exclusions as a default development assumption.

## Documentation Synchronization

- When tool count changes, update both:
  - `docfx/reference/tools/index.md`
  - `docfx/zh-tw/reference/tools/index.md`
- Keep tool descriptions, quickstarts, and E2E templates aligned with current workflow contracts.
- Ad hoc feedback files must remain ignored or under `tmp/`.
- Product documentation belongs under `docfx/` and should be verified with DocFX build plus documentation validation tests.
- Development plans, checklists, audit notes, and one-off reports under `docs/` are local-only by repository rule and must not be staged.
- If quickstart or installer behavior changes, verify the public DocFX endpoint after GitHub Pages deploy when practical.

## Known Product Constraints

- Raw injection requires architecture matching between the server package, bootstrapper, Inspector sidecar, and target process.
- Raw injection is blocked unless `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` includes the exact target executable path.
- Self-contained single-file apps cannot use raw injection, but may use the target-side Inspector SDK host.
- Native AOT apps are not supported.
- Trimmed apps may fail if required dependencies are removed.
- `watch_dp_changes` is registration-only over STDIO; use `wait_for_dp_change` or polling-based verification for bounded waits.
- Elements inside inactive tabs or lazy-rendered subtrees may exist logically but not be rendered in the active visual tree.
- High-volume Agent runs can hit rate limits. Respect structured `RateLimitExceeded` retry metadata and avoid blocking cleanup-sensitive restore or drain steps.
- Event piggyback payloads may include prior context. Use `drain_events` and filters before workflows requiring a clean event buffer.
- `element_screenshot(outputMode="metadata")` may return no image bytes by design; use file/resource mode when pixel evidence is required.

## External References

- Snoop WPF: https://github.com/snoopwpf/snoopwpf
- Model Context Protocol: https://modelcontextprotocol.io/docs
- WPF VisualTreeHelper: https://learn.microsoft.com/dotnet/api/system.windows.media.visualtreehelper
- WPF BindingOperations: https://learn.microsoft.com/dotnet/api/system.windows.data.bindingoperations
