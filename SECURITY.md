# Security

This document describes the security controls that are actually implemented in the current codebase.

## Threat Model

The server can inspect and manipulate live WPF UI state. That means the relevant risks are:

- a malicious or prompt-injected MCP client issuing direct `tools/call` requests
- a local process impersonating the inspector pipe endpoint
- unauthenticated access to UI inspection or mutation tools
- loading an unexpected inspector DLL during `connect`
- leaking secrets or certificates through documentation or local files

The MCP client is untrusted by default. Tool descriptions, annotations, and prompts are guidance only; security decisions are enforced by server-side policy gates before process discovery details, UI text, screenshots, ViewModel values, or runtime mutations are returned.

## Implemented Controls

### 1. DLL signature verification

- `connect` validates the inspector DLL before loading it.
- **Debug builds**: Signature verification is automatically skipped for DLLs within trusted roots (application directory or solution root). No environment variable is needed for local development.
- **Release builds**: Signature verification is always enforced. The Inspector DLL must be Authenticode-signed.
- **Path validation**: Only DLLs within trusted roots (application directory or solution workspace) are accepted. DLLs outside trusted roots are rejected regardless of build configuration or environment variables.

### 1.5 Raw injection target policy

- Raw DLL injection into arbitrary same-user WPF processes is blocked by default.
- The shipping server does not implicitly trust project-scoped targets discovered under the current repository root.
- When the target executable is not explicitly allowlisted, `connect()` fails closed with `errorCode: SecurityError` and `requiresExplicitTargetOptIn: true` instead of injecting if no earlier default-pipe compatibility failure has already stopped the connection attempt.
- If a stale or incompatible default-pipe host is already advertising the expected pipe, `connect()` can return `errorCode: CompatibilityError` before the raw-injection policy denial, but raw injection still remains blocked.
- To allow a specific executable, set `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` to a semicolon-separated list of exact local absolute executable paths; malformed configured entries fail closed with `errorCode: InvalidPolicyConfiguration`.
- Prefer the SDK-hosted path with `InspectorSdk.Initialize()` when you need production diagnostics for an external app and do not want to broaden the raw injection allowlist.

### 1.6 MCP tool and target policy gates

- The server evaluates high-risk MCP `tools/call` requests before dispatching them to tool implementations.
- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` restricts all `connect()` targets to a semicolon-separated list of exact local absolute executable paths. This applies before SDK-hosted reuse or raw injection; unset values fail closed with `SecurityError`, and malformed configured entries fail closed with `InvalidPolicyConfiguration`.
- `get_processes` and `connect()` auto-discovery apply this target policy before returning process names, window titles, architecture/runtime metadata, or candidate details. Denied targets are redacted to aggregate counts.
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` opts into runtime mutation, interaction, render-measurement, and session state-consuming tools such as `set_dp_value`, `click_element`, `execute_command`, `measure_element_render_time`, `capture_state_snapshot`, `restore_state_snapshot`, `drain_events`, and `batch_mutate`.
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` opts into `element_screenshot` at the MCP boundary.
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` opts into target UI text, DependencyProperty and binding values, routed-event payloads, tree/scene summaries, and runtime state snapshots. This is the per-session diagnostic profile gate for read-heavy tools such as `get_ui_summary`, `get_visual_tree`, `get_bindings`, and `get_state_diff`.
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` opts into `get_viewmodel`, `get_commands`, `modify_viewmodel`, and `execute_command`.
- `WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS=true` allows `preview_ui_blueprint` to accept reviewed, content-bound `runtimePackApprovalTokens` for one preview call. Tokens are not persisted and the destructive-tools gate still applies.
- When these boolean gates are unset or false, the affected category fails closed with `errorCode: SecurityError`; malformed boolean values fail closed with `errorCode: InvalidPolicyConfiguration`.

### 1.7 MCP JSON-RPC envelope boundary

The raw MCP JSON-RPC envelope for STDIO requests is parsed by the MCP C# SDK before this server receives typed requests. Pre-dispatch envelope fields such as `id` and `method` on `initialize`, `resources/read`, and `tools/list` are therefore SDK-owned. This project validates tool-call names and arguments after SDK parsing, then validates Inspector IPC request ids, methods, and correlation ids before dispatching requests into the injected or SDK-hosted Inspector host.

Do not treat this as a blanket input-validation gap for tool execution. The project-owned boundary starts at typed MCP request filters and tool wrappers, where oversized tool names, unsupported tools, tool arguments, process target policy, sensitive-read gates, screenshot gates, ViewModel gates, and destructive gates are enforced. The downstream named-pipe IPC boundary also enforces request id, method, correlation id, framing, and authentication constraints.

### 2. Named pipe authentication

- Injection-based `connect` sessions use HMAC challenge-response authentication by default.
- The shared secret must be base64 encoded and decode to at least 32 decoded bytes (256 bits).
- When `WPFDEVTOOLS_AUTH_SECRET` is not set, the server generates a default secret once and reuses it across server restarts for the current user profile.
- Set `WPFDEVTOOLS_AUTH_SECRET` when you need to override the generated secret with a deterministic shared value.
- During injection-based bootstrap, the server writes the short-lived auth-secret handoff file as a DPAPI-protected payload and the native bootstrapper deletes it after loading. This prevents direct plaintext disclosure from the temp file, but code already running as the same Windows user remains inside the local trust boundary.
- The default persisted auth-secret file is `%APPDATA%\WpfDevTools\auth\shared-secret.bin`.
- For `connect()` to reuse an SDK-hosted Inspector, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` together on both sides before calling `InspectorSdk.Initialize()`. The default-hardened MCP server will not reuse a plaintext SDK host.
- If either value is missing, or both are unset, `InspectorSdk.Initialize()` now fails closed instead of starting a plaintext SDK host.

### 3. TLS over named pipes

- Injection-based `connect` sessions use TLS on the pipe connection by default.
- The secure named-pipe transport currently pins TLS 1.2 for compatibility across .NET 8 and .NET Framework 4.8 runtime paths.
- Named-pipe TLS negotiation is verified by `scripts/tests/Test-TlsNegotiation.ps1` for the `net8-net8`, `net8-net48`, and `net48-net8` runtime pairs. Do not enable TLS 1.3 in `SecureTransportProtocols.InspectorTransport` until the same harness proves stable negotiation for every supported pair and the release notes identify the verified Windows/.NET matrix.
- The server creates or reuses a certificate inside that directory.
- If `WPFDEVTOOLS_CERT_DIR` is not set, the server uses the default certificate directory under `%APPDATA%\WpfDevTools\certs`.
- If you set `WPFDEVTOOLS_CERT_DIR`, it must be a local absolute directory. Network paths are not allowed; UNC paths and mapped network drives are rejected.
- Persisted PFX files stay in the protected local certificate directory, but runtime certificate imports use non-exportable private key storage. The transport does not fall back to `Exportable` key imports.
- The client validates the inspector certificate subject and pins the expected thumbprint.
- `WPFDEVTOOLS_CERT_THUMBPRINT` can override the expected thumbprint explicitly.
- `connect()` can reuse an existing SDK-hosted Inspector only when the target app calls `InspectorSdk.Initialize()` with matching `WPFDEVTOOLS_AUTH_SECRET` values and the same local absolute `WPFDEVTOOLS_CERT_DIR` value.
- Even outside SDK-host reuse, any default-pipe `connect()` attempt validates that the named-pipe server is owned by the requested target process and reports a compatible protocol/build fingerprint before the client accepts the connection.
- Before reusing an existing host, the client verifies that the named-pipe server is owned by the requested target process and that the host reports a compatible protocol/build fingerprint.

Package `uninstall` removes client registration. Package `full-uninstall` removes installer-owned payloads and generated registration artifacts, but it does not delete current-user transport state because the same server profile may reuse it across package upgrades. To remove the default persisted auth secret and TLS certificate store intentionally, run:

```powershell
Remove-Item -LiteralPath "$env:APPDATA\WpfDevTools\auth\shared-secret.bin" -Force
Remove-Item -LiteralPath "$env:APPDATA\WpfDevTools\certs" -Recurse -Force
```

### 4. Pipe access limits

- Inspector pipe ACLs are scoped to the current user and SYSTEM.
- Requests are serialized through the pipe client and bounded by message framing limits.
- Session-level request limiting is enforced by the server rate limiter.

## Supported Environment Variables

| Variable | Effect | Recommended usage |
| --- | --- | --- |
| `WPFDEVTOOLS_AUTH_SECRET` | Overrides the generated HMAC authentication secret | Must be base64 encoded and at least 32 decoded bytes (256 bits); set in production when you need deterministic secret rotation or SDK-mode coordination |
| `WPFDEVTOOLS_CERT_DIR` | Overrides the default TLS certificate directory | Use a shared local absolute directory with restricted filesystem permissions when certificate storage must be pinned or shared with SDK mode; network paths are not allowed |
| `WPFDEVTOOLS_CERT_THUMBPRINT` | Pins the expected certificate thumbprint | Use when you need deterministic certificate selection |
| `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` | Explicitly allowlists raw-injection targets | Use a semicolon-separated list of exact local absolute executable paths only when SDK-hosted reuse is not feasible; malformed configured entries fail with `InvalidPolicyConfiguration` |
| `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` | Restricts all `connect()` targets | Required semicolon-separated exact local absolute executable paths; unset or malformed configured entries fail closed |
| `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` | Enables or disables destructive MCP tools | Set `true` only for sessions where runtime mutation, interaction, render measurement, or session state-consuming tools such as `capture_state_snapshot` and `drain_events` are allowed |
| `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS` | Enables or disables screenshot capture | Set `true` only when target UI pixels are allowed to leave the target process |
| `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS` | Enables or disables sensitive runtime read tools | Set `true` only when target UI text, DependencyProperty values, binding data, event payloads, tree/scene summaries, and state snapshots may leave the target process |
| `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` | Enables or disables ViewModel inspection tools | Set `true` only when ViewModel property values may be inspected or commands executed |
| `WPFDEVTOOLS_MCP_ALLOW_COMPOSER_RUNTIME_APPROVALS` | Enables call-scoped Composer runtime approvals | Set `true` only when reviewed exact-content tokens may authorize third-party runtime dependencies for one preview call; tokens are never persisted |

This table lists the security-relevant `WPFDEVTOOLS_*` environment variables for transport, certificate, and raw-injection policy.

## Security contact

Report a suspected vulnerability by opening a private GitHub Security Advisory for the repository owner. Include affected version, commit SHA, reproduction steps, and whether a public release artifact is involved. Do not publish exploit details, screenshots, target UI data, certificate material, or auth secrets in public issues before a maintainer has triaged the report.

Maintainer release checks for public installer paths, checksum publication, runtime policy gates, and snapshot/restore validation live in the DocFX contributor guide: `docfx/contributors/public-path-runtime-security.md`.

## Dependency audit cadence

Before each release candidate, run `dotnet restore --locked-mode` and `dotnet list package --vulnerable` for the solution. Treat NuGet audit warnings as release blockers unless a documented false positive exists. Review `ModelContextProtocol`, `System.Text.Json`, PowerShell packaging dependencies, and GitHub Actions versions against verified advisories. Avoid updates driven only by speculative CVE claims; update dependencies when a verified advisory, compatibility requirement, or pinned-runner change applies.

## Deployment Guidance

### Recommended production posture

1. Keep the default injection-based transport hardening enabled.
2. Set `WPFDEVTOOLS_AUTH_SECRET` when you need deterministic secret rotation or SDK-mode coordination.
3. Set `WPFDEVTOOLS_CERT_DIR` to the same local absolute directory in both processes when certificate storage must be deterministic or shared with SDK mode.
4. Optionally set `WPFDEVTOOLS_CERT_THUMBPRINT` if certificate identity must be fixed explicitly.
5. Keep raw injection disabled by default; use `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` only for explicitly reviewed exact local absolute executable paths.
6. Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to the reviewed exact local absolute executable paths the server may connect to.
7. Disable destructive tools, screenshots, sensitive reads, or ViewModel inspection with the `WPFDEVTOOLS_MCP_ALLOW_*` gates when those capabilities are not needed for the session.

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
- SDK-hosted inspectors require matching transport configuration before `connect()` can reuse the existing host, including the same local absolute `WPFDEVTOOLS_CERT_DIR` value when TLS is enabled. Network paths are not allowed.
- HTTP transport is not part of the current shipping server, so this document covers STDIO plus named-pipe inspector communication only.
