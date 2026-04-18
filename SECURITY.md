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
- **Debug builds**: Signature verification is automatically skipped for DLLs within trusted roots (application directory or solution root). No environment variable is needed for local development.
- **Release builds**: Signature verification is always enforced. The Inspector DLL must be Authenticode-signed.
- **Path validation**: Only DLLs within trusted roots (application directory or solution workspace) are accepted. DLLs outside trusted roots are rejected regardless of build configuration or environment variables.

### 2. Named pipe authentication

- Injection-based `connect` sessions use HMAC challenge-response authentication by default.
- The shared secret must be base64 encoded.
- When `WPFDEVTOOLS_AUTH_SECRET` is not set, the server generates a default secret once and reuses it across server restarts for the current user profile.
- Set `WPFDEVTOOLS_AUTH_SECRET` when you need to override the generated secret with a deterministic shared value.
- For `connect()` to reuse an SDK-hosted Inspector, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` together on both sides. The default-hardened MCP server will not reuse a plaintext SDK host.
- `connect()` can reuse an existing SDK-hosted Inspector when the target app calls `InspectorSdk.Initialize()` with matching `WPFDEVTOOLS_AUTH_SECRET` values.

### 3. TLS over named pipes

- Injection-based `connect` sessions use TLS on the pipe connection by default.
- The server creates or reuses a certificate inside that directory.
- If `WPFDEVTOOLS_CERT_DIR` is not set, the server uses the default certificate directory under `%APPDATA%\WpfDevTools\certs`.
- If you set `WPFDEVTOOLS_CERT_DIR`, it must be an absolute path.
- The client validates the inspector certificate subject and pins the expected thumbprint.
- `WPFDEVTOOLS_CERT_THUMBPRINT` can override the expected thumbprint explicitly.
- `connect()` can reuse an existing SDK-hosted Inspector when the target app calls `InspectorSdk.Initialize()` with the same absolute `WPFDEVTOOLS_CERT_DIR` value.

### 4. Pipe access limits

- Inspector pipe ACLs are scoped to the current user and SYSTEM.
- Requests are serialized through the pipe client and bounded by message framing limits.
- Session-level request limiting is enforced by the server rate limiter.

## Supported Environment Variables

| Variable | Effect | Recommended usage |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Overrides the generated HMAC authentication secret | Set in production when you need deterministic secret rotation or SDK-mode coordination |
| `WPFDEVTOOLS_CERT_DIR` | Overrides the default TLS certificate directory | Use a shared absolute directory with restricted filesystem permissions when certificate storage must be pinned or shared with SDK mode |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected certificate thumbprint | Use when you need deterministic certificate selection |

No other `WPFDEVTOOLS_*` environment variable is currently implemented by the shipping server.

## Deployment Guidance

### Recommended production posture

1. Keep the default injection-based transport hardening enabled.
2. Set `WPFDEVTOOLS_AUTH_SECRET` when you need deterministic secret rotation or SDK-mode coordination.
3. Set `WPFDEVTOOLS_CERT_DIR` to the same absolute directory in both processes when certificate storage must be deterministic or shared with SDK mode.
4. Optionally set `WPFDEVTOOLS_CERT_THUMBPRINT` if certificate identity must be fixed explicitly.

### Secret handling

- Store real secrets outside the repository.
- Keep `.env`, certificate exports, private keys, and password files out of source control.
- Prefer environment injection from your shell, CI secret store, or deployment system.

## Observability

- MCP server logs write to file and stderr.
- Inspector request logs include method name, correlation ID, process ID, duration, and success state.
- When log backpressure drops entries, the logger emits a warning record once capacity becomes available again.

## Current Limitations

- TLS uses locally managed certificates rather than OS-trusted PKI by default.
- SDK-hosted inspectors require matching transport configuration before `connect()` can reuse the existing host, including the same absolute `WPFDEVTOOLS_CERT_DIR` value when TLS is enabled.
- HTTP transport is not part of the current shipping server, so this document covers STDIO plus named-pipe inspector communication only.
