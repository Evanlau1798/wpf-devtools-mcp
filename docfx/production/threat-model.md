# Threat Model

This page summarizes the production threat model for WPF DevTools MCP. It is intentionally concise so external reviewers can map risks to the implemented controls and remaining assumptions.

## Security boundary

WPF DevTools MCP is a local debugging tool. The supported trust boundary is a single Windows user running a reviewed MCP server against reviewed local WPF targets. The server must treat the MCP client as untrusted input, even when the client is a trusted desktop app, because prompts, tool descriptions, and model context can be prompt-injected.

Server-side policy gates are the security decisions. Client guidance, tool annotations, examples, and prompts are usability hints only.

## Threats and mitigations

| Threat | Risk | Mitigations |
| --- | --- | --- |
| MCP client as untrusted or prompt-injected caller | A client may request sensitive reads, screenshots, mutations, or process discovery outside the user's intent. | Target allowlists, sensitive-read gates, screenshot gates, destructive-tool policy gates, structured error contracts, and server-side validation before process metadata disclosure. |
| same-user local attacker | Code running as the same Windows user may inspect local files, environment variables, pipes, or process state. | Local-only path validation, DPAPI-protected default secrets, protected ACLs for persisted auth/cert files, named-pipe authentication, TLS certificate pinning, and redacted default logs. |
| malicious target process | A target may expose misleading UI state, attempt to exhaust diagnostic payloads, or host an incompatible SDK pipe. | Exact target allowlists, runtime compatibility checks, output caps, timeout handling, secure transport validation, and fail-closed SDK transport configuration. |
| fake named-pipe / MITM server | A local process may impersonate an Inspector pipe to capture commands or spoof responses. | Process-derived pipe names, HMAC challenge-response, TLS certificate validation, host compatibility ping, PID validation, and adversarial fake-pipe regression tests. |
| raw injection risk | DLL injection can fail, target the wrong process, or cross architecture/security boundaries. | Exact injection allowlists, architecture preflight before `OpenProcess`, trusted local payload path validation, release signature/integrity checks, and SDK-hosted mode as the preferred path when the target app can opt in. |
| screenshot, ViewModel, and runtime data exfiltration | UI text, screenshots, binding values, ViewModel data, and state snapshots can contain secrets. | `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS`, `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS`, compact text fallback, omitted base64/log text fallback fields, local path redaction, and resource retention limits. |
| supply-chain and release tampering | A modified package or installer could register a malicious MCP server. | Signed payload verification, package hash sidecars, canonical release metadata, SBOM sidecars, package-local integrity checks, and residue checks after uninstall. |
| unsupported future HTTP/SSE multi-session transport | Static process-wide caches and STDIO session assumptions could leak state across clients if reused for multi-session transport. | Current production support is STDIO single-session. HTTP/SSE must not be enabled until session-specific state is moved into DI/request/session scope and covered by isolation tests. |

## Out of scope

- Protecting against an administrator or malware already running with higher privileges than the MCP server.
- Making raw injection safe for arbitrary unreviewed targets.
- Supporting Native AOT or self-contained single-file injection.
- Treating screenshots, ViewModel values, binding values, or state snapshots as non-sensitive.
- Treating future HTTP/SSE transport as production-ready before explicit multi-session isolation work is complete.

## Review checklist

- Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` and `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` contain exact reviewed local executable paths.
- Keep sensitive reads, screenshots, and destructive tools disabled unless the current diagnostic session requires them.
- Prefer SDK-hosted Inspector for apps that can opt in; use raw injection only for reviewed targets and matched architecture.
- Use file or metadata screenshot output for normal diagnostics; avoid inline base64 unless the payload is intentionally small.
- Verify release archives, checksums, signer metadata, and SBOM sidecars before publishing or installing production packages.
