# Production Review Remediation Plan - 2026-04-27

> Keep this file short: completed findings are removed after commit. Git history is the archive.

## Rules

- Use the worktree at `G:\wpf-devtools-mcp\.worktree\production-review-remediation-2026-04-27`.
- Build and test separately: `dotnet build ...`, then `dotnet test ... --no-build`.
- Use strict TDD for behavior changes: RED, GREEN, REFACTOR.
- Commit each completed finding separately with an English commit message and detailed body.
- Do not commit anything under `docs/`.
- Do not split `scripts/online-installer.ps1` in this remediation loop.
- Keep files under 500 lines where feasible; document temporary exceptions.

## Round 1 Review Scores

- Security: 7.0/10.
- Stability and live tests: 7.0/10.
- AI-friendly MCP instructions: 8.0/10.
- DocFX and README accuracy: 7.5/10.
- MCP SDK architecture: 7.5/10.
- Maintainability and testability: 7.0/10.

## Active Work

- [ ] P1: Harden elevated installer paths against reparse points, junctions, symlinks, UNC/network paths, and replacement before destructive file operations.
- [ ] P1: Add server-side MCP policy gates for destructive tools, screenshots, ViewModel inspection, and target allowlisting.
- [ ] P1: Narrow raw-injection implicit repository-root trust to explicit production-safe opt-in or allowlist behavior.
- [ ] P1: Add test ownership governance so new test-created `SessionManager` instances are disposed or intentionally weak-root safe.
- [ ] P1: Make `InspectorSdk` dispatcher initialization use one bounded deadline across the whole wait path.
- [ ] P1: Replace MCP E2E fixed startup sleep with bounded protocol readiness.
- [ ] P1: Strengthen live event and screenshot tests to assert semantic identity and valid image content.
- [ ] P1: Add schema-visible constraints for high-value MCP input parameters.
- [ ] P1: Validate enum and range parameters at the MCP server boundary before pipe requests.
- [ ] P1: Formalize structured-output compatibility strategy with versioned contracts or high-value output schema publication.
- [ ] P1: Replace static MCP tool/helper caches with DI-managed per-host services or prove isolation with regression tests.
- [ ] P1: Add protocol-level cancellation coverage for long-running tools and concurrent `connect` cancellation.
- [ ] P1: Sanitize pipe timeout/reset user-facing errors while preserving diagnostics in logs or structured fields.
- [ ] P1: Extend packaged server smoke tests beyond `initialize` to `tools/list`, `resources/read`, and a safe tool call.
- [ ] P2: Remove blocking waits and sleeps from high-risk lifecycle paths where feasible this loop.
- [ ] P2: Split or isolate event trace state-machine responsibilities without destabilizing behavior.
- [ ] P2: Remove duplicate startup failure signaling in `InspectorHost`.
- [ ] P2: Extract duplicated session attach flow in `SessionManager`.

## Finish Criteria

- [ ] Resolve every remaining active checklist item before starting a new review cycle.
- [ ] Run separated targeted build/test verification after each fix.
- [ ] Run separated full-suite verification on the remediation branch.
- [ ] Dispatch a fresh 5-6 agent objective review after branch verification.
- [ ] Merge to `master` only after branch verification and review are clean.
- [ ] After merge, run separated full-suite verification and dispatch a fresh objective master review.
- [ ] Inspect for residual uncommitted or ignored artifacts before final handoff.