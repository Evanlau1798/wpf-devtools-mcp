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

Set `WPFDEVTOOLS_AUTH_SECRET` to enable HMAC challenge-response authentication.

- The secret must be base64 encoded.
- If the variable is absent, authentication is disabled.

### TLS over named pipes

Set `WPFDEVTOOLS_CERT_DIR` to enable TLS for the inspector connection.

- The server creates or reuses a certificate in that directory.
- The client validates the subject and can pin the expected thumbprint.
- `WPFDEVTOOLS_CERT_THUMBPRINT` can override the expected thumbprint.

### Pipe access limits and server-side controls

- Pipe ACLs are scoped to the current user and SYSTEM.
- Requests are serialized and bounded by framing limits.
- Session-level rate limiting is enforced by the server.

## Recommended production posture

1. Run a `Release` build.
2. Authenticode-sign the inspector DLL.
3. Set `WPFDEVTOOLS_AUTH_SECRET`.
4. Set `WPFDEVTOOLS_CERT_DIR`.
5. Optionally set `WPFDEVTOOLS_CERT_THUMBPRINT`.
6. Restrict who can launch the server on the workstation or VM.

## Important limitations

- Authentication and TLS are opt-in.
- TLS uses locally managed certificates, not an enterprise PKI by default.
- The current shipping transport is STDIO + named-pipe inspector communication; HTTP transport is not part of the current binary.
