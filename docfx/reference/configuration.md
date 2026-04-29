# Configuration Reference

## MCP server runtime variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Overrides the persisted/default HMAC authentication secret | Must be base64 encoded; injection sessions are authenticated by default |
| `WPFDEVTOOLS_CERT_DIR` | Overrides the default TLS certificate directory | Use a protected local directory; injection sessions use TLS by default |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected certificate thumbprint | Optional but useful in locked-down deployments |
| `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` | Allowlists raw-injection target executables | Semicolon-separated exact absolute executable paths; prefer SDK-hosted reuse first |
| `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` | Restricts all MCP `connect()` targets | Required semicolon-separated exact absolute executable paths; unset or malformed configured entries fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` | Enables or disables destructive MCP tool calls | Covers runtime mutation, interaction, and render measurement; accepts `true`/`false`, `1`/`0`, `yes`/`no`, or `on`/`off`; unset, invalid, or false values fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` | Enables or disables `element_screenshot` | Same boolean values as above; unset, invalid, or false values fail closed for screenshot calls |
| `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` | Enables or disables ViewModel inspection tools | Same boolean values as above; unset, invalid, or false values block `get_viewmodel`, `get_commands`, `modify_viewmodel`, and `execute_command` |
| `WPFDEVTOOLS_RATE_LIMIT_RPM` | Overrides the MCP server request rate limit | Positive integer requests per minute |
| `WPFDEVTOOLS_TEXT_FALLBACK_MODE` | Controls MCP `content[0].text` fallback verbosity | Set to `full` to emit full JSON text; unset uses the compact fallback |

## Installer and package variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_SKIP_ELEVATION` | Keeps `run.bat` and installer registration in the current shell | Set to `1` when CLI registration must remain unelevated |
| `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` | Pins the expected release signer by certificate thumbprint | Used by package-local installs when verified release sidecars are not adjacent |
| `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` | Pins the expected release signer by certificate subject | Alternative to thumbprint pinning for package-local installs |
| `WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH` | Enables trusted CLI command path overrides while elevated | Must be `1`; otherwise elevated Claude/Codex path overrides are ignored |
| `WPFDEVTOOLS_CLAUDE_COMMAND_PATH` | Supplies a trusted absolute Claude CLI path | Use only with the elevated opt-in above or in an unelevated trusted shell |
| `WPFDEVTOOLS_CODEX_COMMAND_PATH` | Supplies a trusted absolute Codex CLI path | Use only with the elevated opt-in above or in an unelevated trusted shell |

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
