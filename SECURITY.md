# Security

This document describes the security controls that are actually implemented in the current codebase.

## Threat Model

The server can inspect and manipulate live WPF UI state. That means the relevant risks are:

- a local process impersonating the inspector pipe endpoint
- unauthenticated access to UI inspection or mutation tools
- loading an unexpected inspector DLL during `connect`
- leaking secrets or certificates through documentation or local files

## Implemented Controls

### 1. DLL signature verification

- `connect` validates the inspector DLL before loading it.
- Development-only bypass is available through `WPFDEVTOOLS_SKIP_SIGNATURE_CHECK=1`.
- Do not enable that bypass outside local development.

### 2. Named pipe authentication

- Set `WPFDEVTOOLS_AUTH_SECRET` to enable HMAC challenge-response authentication.
- The shared secret must be base64 encoded.
- If the variable is not set, authentication is disabled.

### 3. TLS over named pipes

- Set `WPFDEVTOOLS_CERT_DIR` to enable TLS on the pipe connection.
- The server creates or reuses a certificate inside that directory.
- The client validates the inspector certificate subject and pins the expected thumbprint.
- `WPFDEVTOOLS_CERT_THUMBPRINT` can override the expected thumbprint explicitly.

### 4. Pipe access limits

- Inspector pipe ACLs are scoped to the current user and SYSTEM.
- Requests are serialized through the pipe client and bounded by message framing limits.
- Session-level request limiting is enforced by the server rate limiter.

## Supported Environment Variables

| Variable | Effect | Recommended usage |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Enables HMAC authentication | Set in production and shared securely between server and inspector |
| `WPFDEVTOOLS_CERT_DIR` | Enables TLS using a local certificate directory | Use a directory with restricted filesystem permissions |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected certificate thumbprint | Use when you need deterministic certificate selection |
| `WPFDEVTOOLS_SKIP_SIGNATURE_CHECK` | Skips DLL signature validation | Local development only |

No other `WPFDEVTOOLS_*` environment variable is currently implemented by the shipping server.

## Deployment Guidance

### Recommended production posture

1. Set `WPFDEVTOOLS_AUTH_SECRET`.
2. Set `WPFDEVTOOLS_CERT_DIR`.
3. Optionally set `WPFDEVTOOLS_CERT_THUMBPRINT` if certificate identity must be fixed explicitly.
4. Keep `WPFDEVTOOLS_SKIP_SIGNATURE_CHECK` unset.

### Secret handling

- Store real secrets outside the repository.
- Keep `.env`, certificate exports, private keys, and password files out of source control.
- Prefer environment injection from your shell, CI secret store, or deployment system.

## Observability

- MCP server logs write to file and stderr.
- Inspector request logs include method name, correlation ID, process ID, duration, and success state.
- When log backpressure drops entries, the logger emits a warning record once capacity becomes available again.

## Current Limitations

- Authentication and TLS are opt-in, not automatic.
- TLS uses locally managed certificates rather than OS-trusted PKI by default.
- HTTP transport is not part of the current shipping server, so this document covers STDIO plus named-pipe inspector communication only.
