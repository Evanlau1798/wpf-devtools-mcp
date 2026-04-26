# Configuration Reference

## MCP server runtime variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Enables HMAC challenge-response authentication | Must be base64 encoded |
| `WPFDEVTOOLS_CERT_DIR` | Enables TLS for the inspector pipe | Use a protected local directory |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected certificate thumbprint | Optional but useful in locked-down deployments |
| `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` | Allowlists external raw-injection target executables | Semicolon-separated exact absolute executable paths; prefer SDK-hosted reuse first |
| `WPFDEVTOOLS_RATE_LIMIT_RPM` | Overrides the MCP server request rate limit | Positive integer requests per minute |

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
