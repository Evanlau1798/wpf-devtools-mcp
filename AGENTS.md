# AGENTS.md

This file provides guidance to Codex and other coding agents working in this repository.

## Language And Communication

- Use Traditional Chinese for user-facing conversation.
- Use English for commit messages.
- Do not use emojis in responses, source comments, or commit messages.
- Keep user updates concise and factual.

## Repository Rules

- Follow production-grade rigor for all code changes.
- Use strict TDD for bug fixes and behavior changes: RED, GREEN, then REFACTOR.
- Keep source and test files under 500 lines. Planning markdown files are exempt.
- Read `.gitignore` before editing it. Only add ignore rules; never remove existing rules.
- Do not commit files under `docs/`. `docfx/` product documentation is commit-able.
- Delete one-off logs and test artifacts or keep them under ignored `tmp/`.
- Build and test in separate commands to avoid file locks.
- Prefer `apply_patch` for manual edits.
- Avoid PowerShell commands that can corrupt Chinese text encoding.
- Complete, verify, and commit each supplied finding separately. Use detailed English conventional commits.
- Do not push a `codex/` branch. Merge to `master` before any authorized push.

## Current Product Contract

- The server exposes 77 MCP tools.
- `connect()` is the default entry workflow and supports auto-discovery.
- `get_processes(windowFilter)` is for disambiguation or explicit window scoping.
- `get_ui_summary` defaults to `depthMode: "semantic"`.
- Scene-level tools are the primary workflow.
- `wait_for_dp_change` is the polling-friendly STDIO fallback for bounded waits.
- Raw injection requires both allowed-target environment variables to contain the exact target executable path.
- Sensitive scene, tree, binding, dependency-property, and state reads require the sensitive-read gate.
- Mutations must use the minimum required gates and snapshot/diff/restore discipline.

## Default MCP Workflow

1. `connect()`
2. `get_active_process`
3. Use scene, tree, diagnostic, or mutation-safe tools.
4. Use `ping` only for an explicit health check.

For disambiguation, call `get_processes(windowFilter)`, then `connect(processId)`. Only call
`select_active_process(processId)` when explicitly switching away from the active process.

## Scene And Mutation Workflows

Scene-first inspection:

1. `get_ui_summary`
2. `get_element_snapshot`
3. `get_form_summary` or `diagnose_visibility`
4. Use atomic tree, binding, DP, or event tools only when deeper inspection is needed.

Mutation safety:

1. `capture_state_snapshot`
2. Perform the interaction.
3. `get_state_diff`
4. `restore_state_snapshot`

A snapshot must include at least one property, ViewModel property, or focus. Empty arguments are
expected to return `MissingRequiredParameter`.

## Agent-Driven E2E Hard Gates

- Keep formal E2E at NO-GO until product P1/P2 findings, non-E2E verification, and the evidence
  validator gates are complete.
- A real Agent E2E must read the current E2E validation task before starting and must call the
  installed MCP server over newline-delimited STDIO JSON-RPC. Build output and source inspection do
  not prove E2E success.
- Keep all run evidence under one ignored evidence root. Use manifest schema
  `wpfdevtools.e2e-run-evidence.v1`; every declared artifact path must stay inside that root and
  carry a verified SHA-256.
- Run `scripts/e2e/Test-E2ERunEvidence.ps1 -Phase PreJudge` before visual judging. Run `-Phase Final`
  after the judge, report, and cleanup evidence exist. Both phases are fail-closed.
- Final output is limited to `runnerCompleted`, `operationalGatesPassed`, `visualQualified`,
  `overallResult`, `reasons`, and `repairBudgetExhausted`. Only a completed runner, all operational,
  report, and cleanup gates, and strict visual scores greater than 9.5 may produce `PASS`.
- The actual launcher must emit one complete JSON event per line using `System.Text.Json` and UTF-8
  without a BOM. A validator fixture does not close this risk; validate a real launcher sample.
- Positive MCP evidence requires `isError=false`, `structuredContent.success=true`, and a proved
  semantic postcondition. Preview readiness requires all success flags, no truncation, and zero
  attention-required items.

## E2E Interactive Contract

- Build the inventory from the union of core-journey checkpoints.
- Include every visible, enabled, hit-testable, loaded, app-authored interactive control with a
  unique `x:Name` or Composer correlation identity.
- Only template-generated parts, OS chrome, hidden, disabled, or non-hit-testable controls may be
  excluded, and every exclusion needs evidence.
- Buttons, menus, and navigation actions require real `ICommand` and `CommandParameter` bindings.
- Lists and selectors require real collection/`ItemsSource` and selection-to-ViewModel bindings.
- Text, toggle, slider, date, and value controls require their primary state property binding.
- `ScrollViewer` may use `native-state-only`, but before/after offsets must prove real interaction.
- Repeated selector containers are covered by selector binding and representative selection. An
  app-authored command inside a data template still needs independent evidence.
- Execute one core journey: locate scene, select a meaningful item, verify detail and selection VM,
  execute the primary bound command, verify visible and VM feedback, perform needed secondary
  interaction, diff state, restore, then verify selection, state, and focus.
- Smoke every remaining eligible control at least once with MCP-native interaction. Do not use
  Computer Use or OS synthetic input.

## Blind Visual Judge Contract

- The judge is pixels-only, blind, isolated, and never decides E2E qualification or repair budget.
- Before invocation, copy prepared reference and candidate images into the attempt-local `inputs/`
  directory, hash the frozen copies, write `visual-judge-inputs.json`, and pass only frozen paths.
- Final validation owns visual thresholds, repair-budget exhaustion, and overall result.
- The canonical reference is the app-only crop from the 1920x1279 source at
  `x=0,y=0,width=1920,height=1215`; keep the app titlebar and remove the Windows taskbar.
- Candidate screenshots must use app-window scope, stay within 1% aspect-ratio error, and use the
  largest matching size that fits the work area.
- Unlimited contract recovery is allowed before attempt 1 but never enters the judge. After attempt
  1, allow at most one aesthetic repair. Attempt 2 must retain the same visual-contract hash.

## Release Discipline

- Prefer GitHub pre-release assets for public-path E2E. Do not create or promote a stable release
  without explicit authorization.
- Public-path E2E installs through the online installer and release assets, not `git clone`.
- Before public E2E, merge to `master`; only push `master` after explicit release authorization.
- After an authorized push, wait for CI/CD and DocFX, then use a fresh Agent for formal E2E.
- Do not confuse the installer alias with the DocFX endpoint.

## Build And Test Commands

Build and test each project separately:

```powershell
dotnet build tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj
dotnet test tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj --no-build

dotnet build tests\WpfDevTools.Tests.Integration\WpfDevTools.Tests.Integration.csproj
dotnet test tests\WpfDevTools.Tests.Integration\WpfDevTools.Tests.Integration.csproj --no-build

dotnet build tests\WpfDevTools.Tests.Unit.Release\WpfDevTools.Tests.Unit.Release.csproj
dotnet test tests\WpfDevTools.Tests.Unit.Release\WpfDevTools.Tests.Unit.Release.csproj --no-build
```

For DocFX validation:

```powershell
dotnet tool run docfx docfx\docfx.json
powershell -ExecutionPolicy Bypass -File scripts\ci\Test-DocFxDocumentation.ps1 -RepoRoot .
```

## Worktree And Handoff Discipline

- Use a dedicated worktree for multi-step or risky work.
- Preserve unrelated user changes and avoid destructive git commands.
- Before committing, inspect status, working diff, staged diff, and stage only relevant files.
- Report changed files, commit SHAs, fresh verification, and remaining production-path risks.
- If another Agent performs E2E or review, capture its report path and close the Agent after it
  returns.

## Documentation And Runtime Notes

- Product documentation belongs under `docfx/`; development plans and one-off reports remain local.
- Keep English and Traditional Chinese DocFX contracts synchronized.
- Use newline-delimited JSON-RPC over STDIO; do not use `Content-Length` framing.
- Treat sandbox-only named-pipe timeouts as environment evidence before declaring product regression.
- High-volume runs must honor structured rate-limit retry metadata without skipping restore or drain.
- `element_screenshot(outputMode="metadata")` intentionally returns no image bytes; use file/resource
  mode for pixel evidence.
