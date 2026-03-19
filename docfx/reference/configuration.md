# Configuration Reference

## Shipping environment variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Enables HMAC challenge-response authentication | Must be base64 encoded |
| `WPFDEVTOOLS_CERT_DIR` | Enables TLS for the inspector pipe | Use a protected local directory |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected certificate thumbprint | Optional but useful in locked-down deployments |

No other `WPFDEVTOOLS_*` variable should be assumed to exist unless it is documented in the shipping codebase.

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
