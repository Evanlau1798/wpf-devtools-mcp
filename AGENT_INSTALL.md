# Agent Install Contract

This file is the raw-link-friendly contract for AI agents that help a user install WPF DevTools MCP. The public guide is [docfx/guides/agent-assisted-install.md](docfx/guides/agent-assisted-install.md). Maintainer release qualification belongs in [RELEASING.md](RELEASING.md), not in user setup flow.

## Boundary

Do not install, download release assets, or modify client configuration until the user has reviewed and approved a concrete plan.

Supported client ids:

- `claude-code`
- `codex`
- `cursor`
- `vscode`
- `visual-studio`
- `claude-desktop`
- `other`

## Required flow

1. Confirm the host is Windows and detect `x64`, `x86`, or `arm64`.
2. Run read-only installer discovery:

   ```powershell
   pwsh -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson
   ```

   If PowerShell 7 is unavailable, use Windows PowerShell with the same read-only action:

   ```powershell
   powershell.exe -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson
   ```

3. Present the plan: version, release channel, architecture, install root, client id, detected clients, and whether registration will be written.
4. Ask for explicit user confirmation before any mutation.
5. Acquire the release package and sidecars for the selected version and architecture.
6. Verify the archive hash, release metadata, SBOMs, and signer pin policy.
7. Run the approved install path only after confirmation.

## Release artifacts to verify

For manual production review, keep these files adjacent to the archive before extraction:

- `release_<version>_win-<arch>.zip`
- `SHA256SUMS.txt`
- `release-assets.json`
- `release-sbom.spdx.json`
- `package-sbom.spdx.json`

`release-sbom.spdx.json` describes the release asset/archive inventory. `package-sbom.spdx.json` describes the package, dependency, script, assembly, and payload contents. None of these files replaces signer trust; production payload signature verification still requires the independent `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` trust root. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only an additional constraint after the thumbprint is pinned.

## Approved install command shapes

Preview pre-release alias until the first stable GitHub Release is published:

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

Current public onboarding uses `-Prerelease`; after the first stable GitHub Release is published, stable installs can omit that switch.

Reviewed local package command after user approval:

```powershell
pwsh -NoProfile -File .\scripts\online-installer.ps1 `
  -PackageArchivePath .\release\release_<version>_win-<arch>.zip `
  -TrustedReleaseMetadataDirectory .\release `
  -Architecture <arch> `
  -Client <client-id> `
  -InstallRoot "<confirmed-install-root>" `
  -NonInteractive -Force -OutputJson
```

Use `powershell.exe -NoProfile -File` with the same arguments when PowerShell 7 is unavailable on the reviewed Windows host.

Package-local fallback after sidecar verification:

```powershell
.\run.bat
```

Use `run.bat` from the extracted package root when the user wants the package entrypoint instead of direct PowerShell invocation.

## Agent output

Report:

- detected platform and architecture
- selected version, architecture, install root, and client id
- release archive and sidecar paths
- hash, metadata, release SBOM, package SBOM, and signer-pin verification results
- exact install command approved by the user
- installed executable path and generated `client-registration` artifact path

Never print or request private keys, PFX passwords, GitHub secrets, certificate export material, auth secrets, or certificate private-key material.

## Prohibited

- Do not execute remote install commands before matching release assets exist and the user approves the plan.
- Do not modify client configuration before confirmation.
- Do not use a source-tree server executable as the installed package path.
- Do not call self-signed certificates production-trusted; they are only for local development.

## Troubleshooting note

If a reviewed local script is blocked by local execution policy, inspect the script contents first and use a process-scoped policy override only from a trusted shell. Do not make policy override the normal install path.
