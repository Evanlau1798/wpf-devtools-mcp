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

- [x] P1: Harden elevated installer paths against reparse points, junctions, symlinks, UNC/network paths, and replacement before destructive file operations.
- [x] P1: Strengthen live event and screenshot tests to assert semantic identity and valid image content.
- [x] P1: Add schema-visible constraints for high-value MCP input parameters.
- [x] P1: Validate enum and range parameters at the MCP server boundary before pipe requests.
- [x] P1: Formalize structured-output compatibility strategy with versioned contracts or high-value output schema publication.
- [x] P1: Replace static MCP tool/helper caches with DI-managed per-host services or prove isolation with regression tests.
- [x] P1: Add protocol-level cancellation coverage for long-running tools and concurrent `connect` cancellation.
- [x] P1: Sanitize pipe timeout/reset user-facing errors while preserving diagnostics in logs or structured fields.
- [x] P2: Remove blocking waits and sleeps from high-risk lifecycle paths where feasible this loop.
- [x] P2: Split or isolate event trace state-machine responsibilities without destabilizing behavior.
- [x] P2: Extract duplicated session attach flow in `SessionManager`.

## Finish Criteria

- [x] Resolve every remaining active checklist item before starting a new review cycle.
- [x] Run separated targeted build/test verification after each fix.
- [x] Run separated full-suite verification on the remediation branch before the final review-fix patch set.
- [x] Run final short verification after the final review-fix patch set: full solution build, focused unit tests, and focused integration tests.
- [x] Skip any fresh review-agent cycle per 2026-04-29 user instruction.
- [ ] Merge to `master` after focused verification is clean.
- [ ] Inspect for residual uncommitted or ignored artifacts before final handoff.

## Verification Notes

- Pre-final-patch full unit suite passed: 3074/3074.
- Pre-final-patch full integration suite passed: 301/301.
- Final full unit suite was intentionally stopped by the user after exceeding one hour.
- Final focused verification passed:
	- `dotnet build WpfDevTools.sln -m:1`
	- `dotnet test tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj --no-build --filter "FullyQualifiedName~McpTargetPolicyTests|FullyQualifiedName~McpToolExecutionPolicyTests|FullyQualifiedName~ConnectToolErrorCodeTests|FullyQualifiedName~ConnectToolSecurityErrorTests|FullyQualifiedName~ConnectToolRawInjectionPolicyTests|FullyQualifiedName~ResponseContractResourceTests|FullyQualifiedName~InstallerUninstallBehaviorTests|FullyQualifiedName~InstallerBootstrapTests|FullyQualifiedName~RepositoryHygieneTests"`
	- `dotnet test tests\WpfDevTools.Tests.Integration\WpfDevTools.Tests.Integration.csproj --no-build --filter "FullyQualifiedName~ConnectAutoDiscoverySelectionTests|FullyQualifiedName~BootstrapInjectionTests"`