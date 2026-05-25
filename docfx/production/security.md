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
- Set `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` to a semicolon-separated list of exact local absolute executable paths only when raw injection into a specific app is an intentional production decision; malformed configured entries fail closed with `errorCode: InvalidPolicyConfiguration`.
- Prefer the SDK-hosted reuse path with `InspectorSdk.Initialize()` when you need production diagnostics for an external target without broadening raw injection scope.

### MCP tool and target policy gates

The server evaluates high-risk MCP `tools/call` requests before dispatching them to tool implementations.

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` restricts all `connect()` targets to exact local absolute executable paths and applies before SDK-hosted reuse or raw injection. Unset values fail closed with `SecurityError`; malformed configured entries fail closed with `InvalidPolicyConfiguration`.
- `get_processes` and `connect()` auto-discovery apply this target policy before returning process names, window titles, architecture/runtime metadata, or candidate details. Denied targets are redacted to aggregate counts.
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` opts into runtime mutation, interaction, render-measurement, and session state-consuming tools, including `set_dp_value`, `click_element`, `execute_command`, `measure_element_render_time`, `capture_state_snapshot`, `restore_state_snapshot`, `drain_events`, and `batch_mutate`.
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` opts into `element_screenshot` at the MCP boundary.
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` opts into target UI text, DependencyProperty and binding values, routed-event payloads, tree/scene summaries, and runtime state snapshots. This is the per-session diagnostic profile gate for read-heavy tools such as `get_ui_summary`, `get_visual_tree`, `get_bindings`, and `get_state_diff`.
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` opts into `get_viewmodel`, `get_commands`, `modify_viewmodel`, and `execute_command`.
- Unset, false, or invalid boolean gates fail closed for the affected category.

Screenshot capture is additionally bounded by resource lifecycle controls. `element_screenshot` defaults to metadata-only output. Inline `base64` output is capped for small PNG payloads; larger pixel captures must use `outputMode: "file"`, which returns a `wpf://screenshots/{screenshotId}` resource handle instead of a local path. File-mode resources expire after 24 hours, are capped at 100 retained screenshots per MCP server session, delete their retained PNG files when evicted or expired, and are purged when the target session disconnects or the server session manager is disposed. `full-uninstall` also removes the default current-user screenshot cache under `%LOCALAPPDATA%\WpfDevTools\tmp\screenshots`; auth secrets and certificates remain intentionally manual cleanup items.

## Safe deployment profiles

Use these profiles as deployment templates for production or shared test workstations. Every target path must be an exact local absolute executable path. Boolean gates not listed for a profile should stay unset or `false`. Prefer SDK-hosted reuse with matching `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` for the first four profiles; use raw injection only for the emergency profile after separate approval.

| Profile | Intended use | Set these gates | Expected blocked tools |
|---|---|---|---|
| Read-only diagnostics | Scene, tree, binding, DP, and state reads where target UI text may leave the process. | `WPFDEVTOOLS_MCP_ALLOWED_TARGETS=<exact target exe>`; `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true`. Keep `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS`, `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION`, `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS`, and `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` unset. | `element_screenshot`, `get_viewmodel`, `modify_viewmodel`, `set_dp_value`, `click_element`, `batch_mutate`, and raw-injection fallback are blocked. `get_ui_summary` and other sensitive read tools are allowed only for the allowlisted target. |
| Screenshot-enabled diagnostics | Pixel capture for a reviewed target when metadata and scene summaries are insufficient. | Read-only diagnostics gates plus `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true`. Use `element_screenshot` `outputMode: "metadata"` or `"file"` by default. | ViewModel tools, mutation tools such as `modify_viewmodel`, `set_dp_value`, `click_element`, and `batch_mutate`, and raw-injection fallback remain blocked. |
| ViewModel-enabled diagnostics | Inspect commands and ViewModel state without mutating the running app. | Read-only diagnostics gates plus `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true`. Keep `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` unset. | Destructive ViewModel and UI changes remain blocked, including `modify_viewmodel`, `execute_command`, `set_dp_value`, `click_element`, and `batch_mutate`. |
| Mutation-enabled test session | Controlled test session where rollback-safe UI or ViewModel changes are expected. | Read-only diagnostics gates plus `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true`; add `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` only when `modify_viewmodel` or `execute_command` is required; add screenshots only if pixel evidence is required. | Any capability whose gate stays unset is blocked. Raw-injection fallback remains blocked unless the emergency profile is also explicitly approved. |
| Raw-injection emergency diagnostics | Last-resort diagnostics for a reviewed local target that cannot host the SDK inspector. | `WPFDEVTOOLS_MCP_ALLOWED_TARGETS=<exact target exe>` and `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS=<same exact target exe>`; then add only the minimum `WPFDEVTOOLS_MCP_ALLOW_*` gates from the profiles above. | Non-allowlisted targets are blocked. `element_screenshot`, `get_viewmodel`, `modify_viewmodel`, `set_dp_value`, `click_element`, and `batch_mutate` remain blocked unless their exact profile gates are also enabled. |

### Named-pipe authentication

Injection-based `connect` sessions use HMAC challenge-response authentication by default.

- The secret must be base64 encoded.
- When `WPFDEVTOOLS_AUTH_SECRET` is not set, the server generates a default secret once and reuses it across server restarts for the current user profile.
- Set `WPFDEVTOOLS_AUTH_SECRET` when you need to override the generated secret with a deterministic shared value.
- During injection-based bootstrap, the server writes the short-lived auth-secret handoff file as a DPAPI-protected payload and the native bootstrapper deletes it after loading. This prevents direct plaintext disclosure from the temp file, but code already running as the same Windows user remains inside the local trust boundary.
- The default persisted auth-secret file is `%APPDATA%\WpfDevTools\auth\shared-secret.bin`.
- For `connect()` to reuse an SDK-hosted Inspector, set `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` together on both sides before calling `InspectorSdk.Initialize()`. The default-hardened MCP server will not reuse a plaintext SDK host.
- If either value is missing, or both are unset, `InspectorSdk.Initialize()` now fails closed instead of starting a plaintext SDK host.

### TLS over named pipes

Injection-based `connect` sessions use TLS for the inspector connection by default.

- The secure named-pipe transport currently pins TLS 1.2 for compatibility across .NET 8 and .NET Framework 4.8 runtime paths.
- Named-pipe TLS negotiation is verified by `scripts/tests/Test-TlsNegotiation.ps1` for the `net8-net8`, `net8-net48`, and `net48-net8` runtime pairs. Do not enable TLS 1.3 in `SecureTransportProtocols.InspectorTransport` until the same harness proves stable negotiation for every supported pair and the release notes identify the verified Windows/.NET matrix.
- The server creates or reuses a certificate in that directory.
- If `WPFDEVTOOLS_CERT_DIR` is not set, the server uses the default certificate directory under `%APPDATA%\WpfDevTools\certs`.
- If you set `WPFDEVTOOLS_CERT_DIR`, it must be a local absolute directory. Network paths are not allowed; UNC paths and mapped network drives are rejected.
- Persisted PFX files stay in the protected local certificate directory, but runtime certificate imports use non-exportable private key storage. The transport does not fall back to `Exportable` key imports.
- The client validates the subject and pins the expected thumbprint.
- `WPFDEVTOOLS_CERT_THUMBPRINT` can override the expected thumbprint.
- `connect()` can reuse an existing SDK-hosted Inspector only when the target app calls `InspectorSdk.Initialize()` with matching `WPFDEVTOOLS_AUTH_SECRET` values and the same local absolute `WPFDEVTOOLS_CERT_DIR` value.
- Even outside SDK-host reuse, any default-pipe `connect()` attempt validates that the named-pipe server is owned by the requested target process and reports a compatible protocol/build fingerprint before the client accepts the connection.
- Before reusing an existing host, the client verifies that the named-pipe server is owned by the requested target process and that the host reports a compatible protocol/build fingerprint.

Package `uninstall` removes client registration. Package `full-uninstall` removes installer-owned payloads and generated registration artifacts, but it does not delete current-user transport state because the same server profile may reuse it across package upgrades. To remove the default persisted auth secret and TLS certificate store intentionally, run:

```powershell
Remove-Item -LiteralPath "$env:APPDATA\WpfDevTools\auth\shared-secret.bin" -Force
Remove-Item -LiteralPath "$env:APPDATA\WpfDevTools\certs" -Recurse -Force
```

### Pipe access limits and server-side controls

- Pipe ACLs are scoped to the current user and SYSTEM.
- Requests are serialized and bounded by framing limits.
- Session-level rate limiting is enforced by the server.
- Tool policy gates can block destructive tools, screenshots, sensitive reads, ViewModel inspection, and non-allowlisted targets before any target-process request is sent.

## Recommended production posture

1. Run a `Release` build.
2. Authenticode-sign the inspector DLL.
3. Keep the default injection-based transport hardening enabled.
4. Set `WPFDEVTOOLS_AUTH_SECRET` when you need deterministic secret rotation or SDK-mode coordination.
5. Set `WPFDEVTOOLS_CERT_DIR` to the same local absolute directory in both processes when certificate storage must be deterministic or shared with SDK mode.
6. Optionally set `WPFDEVTOOLS_CERT_THUMBPRINT`.
7. Keep raw injection disabled by default; use `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` only for explicitly reviewed exact local absolute executable paths.
8. Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to the reviewed exact local absolute executable paths the server may connect to.
9. Disable destructive tools, screenshots, sensitive reads, or ViewModel inspection with the `WPFDEVTOOLS_MCP_ALLOW_*` gates when those capabilities are not needed.
10. Restrict who can launch the server on the workstation or VM.

## Important limitations

- TLS uses locally managed certificates, not an enterprise PKI by default.
- SDK-hosted inspectors require matching transport configuration before `connect()` can reuse the existing host, including the same local absolute `WPFDEVTOOLS_CERT_DIR` value when TLS is enabled. Network paths are not allowed.
- The current shipping transport is STDIO + named-pipe inspector communication; HTTP transport is not part of the current binary.
