# Configuration Reference

## MCP server runtime variables

### WPFDEVTOOLS_AUTH_SECRET

- Purpose: Overrides the persisted/default HMAC authentication secret.
- Notes: Must be base64 encoded and at least 32 decoded bytes (256 bits). Injection sessions are authenticated by default.

### WPFDEVTOOLS_CERT_DIR

- Purpose: Overrides the default TLS certificate directory.
- Notes: Use a protected local directory. Injection sessions use TLS by default.

### WPFDEVTOOLS_CERT_THUMBPRINT

- Purpose: Pins the expected certificate thumbprint.
- Notes: Optional but useful in locked-down deployments.

### WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS

- Purpose: Allowlists raw-injection target executables.
- Notes: Use semicolon-separated exact local absolute executable paths. Malformed configured entries fail with `InvalidPolicyConfiguration`. Prefer SDK-hosted reuse first.

### WPFDEVTOOLS_MCP_ALLOWED_TARGETS

- Purpose: Restricts all MCP `connect()` targets.
- Notes: Required semicolon-separated exact local absolute executable paths. Unset values fail with `SecurityError`; malformed configured entries fail with `InvalidPolicyConfiguration`.

### WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS

- Purpose: Enables or disables destructive MCP tool calls.
- Notes: Covers runtime mutation, interaction, render measurement, and session state-consuming tools such as `capture_state_snapshot` and `drain_events`. Accepts `true`/`false`, `1`/`0`, `yes`/`no`, or `on`/`off`; unset, invalid, or false values fail closed.

### WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS

- Purpose: Enables or disables `element_screenshot`.
- Notes: Uses the same boolean values as above. Unset, invalid, or false values fail closed for screenshot calls.

### WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS

- Purpose: Enables or disables sensitive runtime read tools.
- Notes: Covers target UI text, DependencyProperty and binding values, routed-event payloads, tree/scene summaries, and state snapshots. Uses the same boolean values as above; unset, invalid, or false values fail closed.

### WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION

- Purpose: Enables or disables ViewModel inspection tools.
- Notes: Uses the same boolean values as above. Unset, invalid, or false values block `get_viewmodel`, `get_commands`, `get_datacontext_chain`, `modify_viewmodel`, and `execute_command`. The same gate applies when `capture_state_snapshot` requests `viewModelPropertyNames`, when `batch_mutate` captures or mutates ViewModel state, and when `wait_for_dp_change_after_mutation` uses a ViewModel mutation trigger.

### WPFDEVTOOLS_MCP_SKIP_EXISTING_HOST_REUSE

- Purpose: Skips the existing SDK-hosted Inspector reuse probe during `connect()`.
- Notes: Diagnostics only. Leave unset for normal production usage so SDK-hosted reuse remains the preferred path.

### WPFDEVTOOLS_RATE_LIMIT_RPM

- Purpose: Overrides the MCP server request rate limit.
- Notes: Positive integer requests per minute. The default is 300; values above 10000 are clamped to 10000.

### WPFDEVTOOLS_TEXT_FALLBACK_MODE

- Purpose: Controls MCP `content[0].text` fallback verbosity.
- Notes: Set to `full` only for legacy text-only clients. Large or sensitive payload fields such as base64 screenshots and log dumps remain omitted from the text fallback. Unset uses the compact fallback.

`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` is not limited to direct ViewModel tools. It is also required whenever a snapshot, batch operation, or wait-after-mutation trigger requests ViewModel state.

The internal per-process rate limiter cache is capped at 1000 entries and evicts the least recently used entries when full. This cache capacity is not externally configurable.

`WPFDEVTOOLS_MCP_SKIP_EXISTING_HOST_REUSE` is intended for diagnosing SDK-host reuse behavior. Do not set it in normal onboarding or production deployment unless you are intentionally validating the raw-injection fallback.

## Installer and package variables

### WPFDEVTOOLS_SKIP_ELEVATION

- Purpose: Keeps `run.bat` and installer registration in the current shell.
- Notes: Set to `1` when CLI registration must remain unelevated.

### WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT

- Purpose: Pins the expected release signer by certificate thumbprint.
- Notes: Required independent trust root for production payload signature verification. Sidecars do not replace it.

### WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT

- Purpose: Adds an expected release signer certificate subject check.
- Notes: Additional constraint only. Package-local installs still require a thumbprint trust root.

### WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY

- Purpose: Points portable checksum-only package runs to trusted release metadata.
- Notes: Use only when running a package-local `run.bat` or `bin\wpf-devtools-<arch>.exe` away from the original release sidecars. The directory must contain the original archive and checksum metadata such as `SHA256SUMS.txt`. This does not replace `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` for signed production packages.

### WPFDEVTOOLS_CLAUDE_COMMAND_PATH

- Purpose: Supplies a trusted absolute Claude CLI path.
- Notes: Use only from a trusted unelevated shell. Elevated installer runs reject environment-provided CLI command paths.

### WPFDEVTOOLS_CODEX_COMMAND_PATH

- Purpose: Supplies a trusted absolute Codex CLI path.
- Notes: Use only from a trusted unelevated shell. Elevated installer runs reject environment-provided CLI command paths.

No other `WPFDEVTOOLS_*` variable should be assumed to exist unless it is documented in the shipping codebase or a release-only maintainer guide.

## Build modes

### Debug

- best for local development
- trusted local roots can skip signature verification
- easiest path for unsigned local builds

### Release

- intended for production deployment
- signature verification is enforced
- pair with code signing and explicit security configuration

## Architecture-specific builds

Use `-p:Platform=` for managed builds and `msbuild /p:Platform=` for the native bootstrapper.
