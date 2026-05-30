# Configuration Reference

## MCP server runtime variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Overrides the persisted/default HMAC authentication secret | Must be base64 encoded; injection sessions are authenticated by default |
| `WPFDEVTOOLS_CERT_DIR` | Overrides the default TLS certificate directory | Use a protected local directory; injection sessions use TLS by default |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected certificate thumbprint | Optional but useful in locked-down deployments |
| `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` | Allowlists raw-injection target executables | Semicolon-separated exact local absolute executable paths; malformed configured entries fail with `InvalidPolicyConfiguration`; prefer SDK-hosted reuse first |
| `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` | Restricts all MCP `connect()` targets | Required semicolon-separated exact local absolute executable paths; unset values fail with `SecurityError`, malformed configured entries fail with `InvalidPolicyConfiguration` |
| `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` | Enables or disables destructive MCP tool calls | Covers runtime mutation, interaction, render measurement, and session state-consuming tools such as `capture_state_snapshot` and `drain_events`; accepts `true`/`false`, `1`/`0`, `yes`/`no`, or `on`/`off`; unset, invalid, or false values fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` | Enables or disables `element_screenshot` | Same boolean values as above; unset, invalid, or false values fail closed for screenshot calls |
| `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS` | Enables or disables sensitive runtime read tools | Covers target UI text, DependencyProperty and binding values, routed-event payloads, tree/scene summaries, and state snapshots; same boolean values as above; unset, invalid, or false values fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` | Enables or disables ViewModel inspection tools | Same boolean values as above; unset, invalid, or false values block `get_viewmodel`, `get_commands`, `modify_viewmodel`, and `execute_command` |
| `WPFDEVTOOLS_RATE_LIMIT_RPM` | Overrides the MCP server request rate limit | Positive integer requests per minute; default is 300; values above 10000 are clamped to 10000 |
| `WPFDEVTOOLS_TEXT_FALLBACK_MODE` | Controls MCP `content[0].text` fallback verbosity | Set to `full` only for legacy text-only clients; large or sensitive payload fields such as base64 screenshots and log dumps remain omitted from the text fallback. Unset uses the compact fallback |

The internal per-process rate limiter cache is capped at 1000 entries and evicts the least recently used entries when full. This cache capacity is not externally configurable.

## Installer and package variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_SKIP_ELEVATION` | Keeps `run.bat` and installer registration in the current shell | Set to `1` when CLI registration must remain unelevated |
| `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` | Pins the expected release signer by certificate thumbprint | Used by package-local installs when verified release sidecars are not adjacent |
| `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` | Adds an expected release signer certificate subject check | additional constraint only; package-local installs still require a thumbprint trust root |
| `WPFDEVTOOLS_CLAUDE_COMMAND_PATH` | Supplies a trusted absolute Claude CLI path | Use only from a trusted unelevated shell; elevated installer runs reject environment-provided CLI command paths |
| `WPFDEVTOOLS_CODEX_COMMAND_PATH` | Supplies a trusted absolute Codex CLI path | Use only from a trusted unelevated shell; elevated installer runs reject environment-provided CLI command paths |

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
