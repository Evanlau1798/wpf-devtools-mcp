# Security Model

This page documents the controls that are implemented in the current shipping codebase.

## Threat model

The server can inspect and mutate live WPF UI state. That makes these risks relevant:

- unauthorized access to the inspector pipe
- loading an unexpected inspector DLL during `connect`
- leaking credentials or certificates through local files
- man-in-the-middle or impersonation on the named-pipe channel

## Implemented controls

### DLL validation

`connect` validates the inspector DLL before loading it.

- **Debug builds**: trusted local paths skip signature verification to keep local development practical.
- **Release builds**: signature verification is enforced.
- **Path validation**: the shipping server accepts only trusted roots.

### Named-pipe authentication

Injection-based `connect` sessions use HMAC challenge-response authentication by default.

- The secret must be base64 encoded.
- When `WPFDEVTOOLS_AUTH_SECRET` is not set, the server generates a default secret once and reuses it across server restarts for the current user profile.
- Set `WPFDEVTOOLS_AUTH_SECRET` when you need to override the generated secret with a deterministic shared value.
- For `connect()` to reuse an SDK-hosted Inspector, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` together on both sides. The default-hardened MCP server will not reuse a plaintext SDK host.
- `connect()` can reuse an existing SDK-hosted Inspector when the target app calls `InspectorSdk.Initialize()` with matching `WPFDEVTOOLS_AUTH_SECRET` values.

### TLS over named pipes

Injection-based `connect` sessions use TLS for the inspector connection by default.

- The server creates or reuses a certificate in that directory.
- If `WPFDEVTOOLS_CERT_DIR` is not set, the server uses the default certificate directory under `%APPDATA%\WpfDevTools\certs`.
- If you set `WPFDEVTOOLS_CERT_DIR`, it must be an absolute path.
- The client validates the subject and can pin the expected thumbprint.
- `WPFDEVTOOLS_CERT_THUMBPRINT` can override the expected thumbprint.
- `connect()` can reuse an existing SDK-hosted Inspector when the target app calls `InspectorSdk.Initialize()` with the same absolute `WPFDEVTOOLS_CERT_DIR` value.
- Before reusing an existing host, the client verifies that the named-pipe server is owned by the requested target process and that the host reports a compatible protocol/build fingerprint.

### Pipe access limits and server-side controls

- Pipe ACLs are scoped to the current user and SYSTEM.
- Requests are serialized and bounded by framing limits.
- Session-level rate limiting is enforced by the server.

## Recommended production posture

1. Run a `Release` build.
2. Authenticode-sign the inspector DLL.
3. Keep the default injection-based transport hardening enabled.
4. Set `WPFDEVTOOLS_AUTH_SECRET` when you need deterministic secret rotation or SDK-mode coordination.
5. Set `WPFDEVTOOLS_CERT_DIR` to the same absolute directory in both processes when certificate storage must be deterministic or shared with SDK mode.
6. Optionally set `WPFDEVTOOLS_CERT_THUMBPRINT`.
7. Restrict who can launch the server on the workstation or VM.

## Important limitations

- TLS uses locally managed certificates, not an enterprise PKI by default.
- SDK-hosted inspectors require matching transport configuration before `connect()` can reuse the existing host, including the same absolute `WPFDEVTOOLS_CERT_DIR` value when TLS is enabled.
- The current shipping transport is STDIO + named-pipe inspector communication; HTTP transport is not part of the current binary.
