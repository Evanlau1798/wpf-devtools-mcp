# Agent-Assisted Install

Use this page when an AI agent helps a user install WPF DevTools MCP. The agent must plan first, avoid side effects before confirmation, verify release provenance, and call only reviewed local installer entrypoints.

## Agent contract

- Read this guide or `AGENT_INSTALL.md` before proposing commands.
- Do not install yet.
- Present a plan and get explicit approval; the flow requires user confirmation before mutation.
- Never execute a moving-branch raw script or remote one-line install command while public endpoint smoke checks are incomplete.
- Never ask for private keys, PFX password values, GitHub secrets, or signing secrets.
- Never store, print, upload, or forward secrets.

Supported client ids:

- `claude-code`
- `codex`
- `cursor`
- `vscode`
- `visual-studio`
- `claude-desktop`
- `other`

## Required user confirmations

Ask the user to confirm all of the following before running an installer or writing client configuration:

- Version, such as `latest` or a concrete release tag.
- Architecture: `x64`, `x86`, or `arm64`.
- Install root.
- Client registration target: `claude-code`, `codex`, `cursor`, `vscode`, `visual-studio`, `claude-desktop`, or `other`.
- For Cursor, whether registration is global or project-local.
- Whether the installer may update existing registration artifacts.

## Discovery steps

Discovery is read-only:

1. Confirm the OS is Windows.
2. Detect system architecture and whether the target WPF process requires a different architecture.
3. Detect available CLIs or config roots for supported clients.
4. Check whether a previous install root has live evidence.
5. Report findings and wait for confirmation.

## Release acquisition

Resolve the versioned release archive name:

```text
release_<version>_win-<arch>.zip
```

Acquire the matching sidecars:

- `SHA256SUMS.txt`
- `release-assets.json`

Until public endpoint smoke checks pass, prefer a locally generated package or already downloaded release asset. Do not provide a remote one-line install command.

## Provenance verification

Before extraction or install:

1. Verify the archive hash with `SHA256SUMS.txt`.
2. Verify canonical asset metadata with `release-assets.json`.
3. Verify the signed payload against a signer pin.
4. Use `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` for an exact certificate thumbprint pin when the verified sidecars are no longer adjacent.
5. Use `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` only when subject-based policy is explicitly accepted.

The agent should report the signer pin policy and verification result, not certificate secrets.

## Client registration

After confirmation, call the reviewed local `scripts/online-installer.ps1` or the package-local `run.bat`. The generated registration artifacts under `<InstallRoot>\<arch>\client-registration\` are the source of truth.

Inspect the artifact for the selected client:

- CLI registrations for `claude-code` and `codex`.
- JSON registrations for `cursor`, `vscode`, `visual-studio`, and `claude-desktop`.
- Artifact-only guidance for `other`.

## Code signing boundaries

Normal install path:

- Verify published release signatures and signer pin policy.
- Do not handle private keys, PFX password values, GitHub secrets, or certificate export material.

Release signing helper path:

- An agent may explain Authenticode signing concepts and local commands.
- The user must keep the certificate and secrets local.
- self-signed certificates are only for local/dev/test and are not production-trusted.
- After signing local artifacts, regenerate `SHA256SUMS.txt` and `release-assets.json`.

## Troubleshooting

- If the OS is not Windows, stop and report that the server is Windows-only.
- If architecture is uncertain, ask the user to confirm before installing.
- If sidecars are missing, do not install.
- If signer pin validation fails, stop.
- If CLI discovery is blocked by elevation, use generated artifacts and ask the user to register manually.
- If `connect()` fails after install, verify `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`, process architecture, and whether raw injection is allowed for that target.

## Copyable agent prompt

```text
Read AGENT_INSTALL.md or docfx/guides/agent-assisted-install.md. Do not install yet. Detect Windows platform, architecture, available MCP clients, and any existing install root without changing files. Present a plan that includes version, architecture, install root, client id, release archive, SHA256SUMS.txt, release-assets.json, and signer pin policy. Ask for confirmation before mutation. After approval, run only the reviewed local installer or package-local run.bat, inspect generated client-registration artifacts, verify the installed executable, and report results without secrets.
```
