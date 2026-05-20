# Agent Install Contract

This file is the short, raw-link-friendly contract for AI agents. The full guide is [docfx/guides/agent-assisted-install.md](docfx/guides/agent-assisted-install.md).

## Boundary

Do not install yet. First produce a plan, show what will change, and get explicit user confirmation before mutation.

Supported client ids must stay synchronized with the installer:

- `claude-code`
- `codex`
- `cursor`
- `vscode`
- `visual-studio`
- `claude-desktop`
- `other`

## Required Flow

1. Confirm the host is Windows and detect `x64`, `x86`, or `arm64`.
2. Run the read-only installer discovery plan without writing files:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -Action plan -OutputJson
   ```

3. Detect available MCP clients without writing files.
4. Ask the user to confirm version, architecture, install root, and client registration target.
5. Acquire the versioned release archive and sidecars: `release_<version>_win-<arch>.zip`, `SHA256SUMS.txt`, and `release-assets.json`.
6. Verify the archive hash against `SHA256SUMS.txt`.
7. Verify canonical metadata from `release-assets.json`.
8. Enforce signer pin policy with `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`; report the signer pin that was checked.
9. Only after confirmation, call the reviewed local `scripts/online-installer.ps1` or package-local `run.bat`.
10. Inspect generated `client-registration` artifacts and verify the installed executable path.
11. Report changed files or registrations without printing secrets.

## Prohibited

- Do not execute remote install commands while public endpoint smoke checks are incomplete.
- Do not modify client configuration before the user confirms the plan.
- Do not ask for private keys, PFX password values, GitHub secrets, or signing secrets.
- Do not store, print, upload, or forward secrets.
- Do not call self-signed certificates production-trusted; they are only for local/dev/test.

## Output Expected From An Agent

- Detected platform and architecture.
- Detected clients and selected client id.
- Release archive and sidecar paths.
- Hash, metadata, and signer pin verification result.
- The exact local installer command that will run after confirmation.
- Installed executable path and client registration artifact path.
