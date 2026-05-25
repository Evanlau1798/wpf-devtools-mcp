# Security Model

This page documents the controls that are implemented in the current shipping codebase.

## Threat model

The server can inspect and mutate live WPF UI state. That makes these risks relevant:

- a malicious or prompt-injected MCP client issuing direct `tools/call` requests
- unauthorized access to the inspector pipe
- loading an unexpected inspector DLL during `connect`
- leaking credentials or certificates through local files
- man-in-the-middle or impersonation on the named-pipe channel

The MCP client is untrusted by default. Tool descriptions, annotations, and prompts are guidance only; security decisions are enforced by server-side policy gates before process discovery details, UI text, screenshots, ViewModel values, or runtime mutations are returned.

## Implemented controls

### DLL validation

`connect` validates the inspector DLL before loading it.

- **Debug builds**: trusted local paths skip signature verification to keep local development practical.
- **Release builds**: signature verification is enforced.
- **Path validation**: the shipping server accepts only trusted roots.

### Raw injection target policy

Raw DLL injection into arbitrary same-user WPF processes is blocked by default.

- The shipping server does not implicitly trust project-scoped targets discovered under the current repository root.
- When the target executable is not explicitly allowlisted, `connect()` fails closed with `errorCode: SecurityError` and `requiresExplicitTargetOptIn: true` instead of injecting if no earlier default-pipe compatibility failure has already stopped the connection attempt.
- If a stale or incompatible default-pipe host is already advertising the expected pipe, `connect()` can return `errorCode: CompatibilityError` before the raw-injection policy denial, but raw injection still remains blocked.
- Set `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` to a semicolon-separated list of exact local absolute executable paths only when raw injection into a specific app is an intentional production decision.
- Prefer the SDK-hosted reuse path with `InspectorSdk.Initialize()` when you need production diagnostics for an external target without broadening raw injection scope.

### MCP tool and target policy gates

The server evaluates high-risk MCP `tools/call` requests before dispatching them to tool implementations.

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` restricts all `connect()` targets to exact local absolute executable paths, applies before SDK-hosted reuse or raw injection, and fails closed when unset or malformed.
- `get_processes` and `connect()` auto-discovery apply this target policy before returning process names, window titles, architecture/runtime metadata, or candidate details. Denied targets are redacted to aggregate counts.
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` opts into runtime mutation, interaction, render-measurement, and session state-consuming tools, including `set_dp_value`, `click_element`, `execute_command`, `measure_element_render_time`, `capture_state_snapshot`, `restore_state_snapshot`, `drain_events`, and `batch_mutate`.
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` opts into `element_screenshot` at the MCP boundary.
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` opts into `get_viewmodel`, `get_commands`, `modify_viewmodel`, and `execute_command`.
- Unset, false, or invalid boolean gates fail closed for the affected category.

### Named-pipe authentication

Injection-based `connect` sessions use HMAC challenge-response authentication by default.

- The secret must be base64 encoded.
- When `WPFDEVTOOLS_AUTH_SECRET` is not set, the server generates a default secret once and reuses it across server restarts for the current user profile.
- Set `WPFDEVTOOLS_AUTH_SECRET` when you need to override the generated secret with a deterministic shared value.
- During injection-based bootstrap, the server writes the short-lived auth-secret handoff file as a DPAPI-protected payload and the native bootstrapper deletes it after loading. This prevents direct plaintext disclosure from the temp file, but code already running as the same Windows user remains inside the local trust boundary.
- For `connect()` to reuse an SDK-hosted Inspector, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` together on both sides before calling `InspectorSdk.Initialize()`. The default-hardened MCP server will not reuse a plaintext SDK host.
- If either value is missing, or both are unset, `InspectorSdk.Initialize()` now fails closed instead of starting a plaintext SDK host.

### TLS over named pipes

Injection-based `connect` sessions use TLS for the inspector connection by default.

- The secure named-pipe transport currently pins TLS 1.2 for compatibility across .NET 8 and .NET Framework 4.8 runtime paths.
- The server creates or reuses a certificate in that directory.
- If `WPFDEVTOOLS_CERT_DIR` is not set, the server uses the default certificate directory under `%APPDATA%\WpfDevTools\certs`.
- If you set `WPFDEVTOOLS_CERT_DIR`, it must be a local absolute directory. Network paths are not allowed; UNC paths and mapped network drives are rejected.
- Persisted PFX files stay in the protected local certificate directory, but runtime certificate imports use non-exportable private key storage. The transport does not fall back to `Exportable` key imports.
- The client validates the subject and pins the expected thumbprint.
- `WPFDEVTOOLS_CERT_THUMBPRINT` can override the expected thumbprint.
- `connect()` can reuse an existing SDK-hosted Inspector only when the target app calls `InspectorSdk.Initialize()` with matching `WPFDEVTOOLS_AUTH_SECRET` values and the same local absolute `WPFDEVTOOLS_CERT_DIR` value.
- Even outside SDK-host reuse, any default-pipe `connect()` attempt validates that the named-pipe server is owned by the requested target process and reports a compatible protocol/build fingerprint before the client accepts the connection.
- Before reusing an existing host, the client verifies that the named-pipe server is owned by the requested target process and that the host reports a compatible protocol/build fingerprint.

### Pipe access limits and server-side controls

- Pipe ACLs are scoped to the current user and SYSTEM.
- Requests are serialized and bounded by framing limits.
- Session-level rate limiting is enforced by the server.
- Tool policy gates can block destructive tools, screenshots, ViewModel inspection, and non-allowlisted targets before any target-process request is sent.

## Recommended production posture

1. Run a `Release` build.
2. Authenticode-sign the inspector DLL.
3. Keep the default injection-based transport hardening enabled.
4. Set `WPFDEVTOOLS_AUTH_SECRET` when you need deterministic secret rotation or SDK-mode coordination.
5. Set `WPFDEVTOOLS_CERT_DIR` to the same local absolute directory in both processes when certificate storage must be deterministic or shared with SDK mode.
6. Optionally set `WPFDEVTOOLS_CERT_THUMBPRINT`.
7. Keep raw injection disabled by default; use `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` only for explicitly reviewed exact local absolute executable paths.
8. Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to the exact local absolute executable paths the server may connect to.
9. Disable destructive tools, screenshots, or ViewModel inspection with the `WPFDEVTOOLS_MCP_ALLOW_*` gates when those capabilities are not needed.
10. Restrict who can launch the server on the workstation or VM.

## Important limitations

- TLS uses locally managed certificates, not an enterprise PKI by default.
- SDK-hosted inspectors require matching transport configuration before `connect()` can reuse the existing host, including the same local absolute `WPFDEVTOOLS_CERT_DIR` value when TLS is enabled. Network paths are not allowed.
- The current shipping transport is STDIO + named-pipe inspector communication; HTTP transport is not part of the current binary.
