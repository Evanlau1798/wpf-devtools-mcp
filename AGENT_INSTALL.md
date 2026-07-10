# Agent Install Contract

This file is the raw-link-friendly contract for AI agents that help a user install WPF DevTools MCP. The public guide is [docfx/guides/agent-assisted-install.md](docfx/guides/agent-assisted-install.md). Maintainer release qualification belongs in [RELEASING.md](RELEASING.md), not in user setup flow.

## Boundary

Do not install, download release assets, or modify client configuration until the user has reviewed and approved a concrete plan.

Supported client ids:

- `claude-code`
- `codex`
- `grok`
- `cursor`
- `vscode`
- `visual-studio`
- `claude-desktop`
- `other`

## Required flow

1. Confirm the host is Windows and detect `x64`, `x86`, or `arm64`.
2. Run read-only installer discovery with the public installer entrypoint:

   ```powershell
   & ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Action plan -OutputJson
   ```

   If you are reviewing a checked-out source tree instead of helping a normal user, the local script path is also valid:

   ```powershell
   pwsh -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson
   ```

   If PowerShell 7 is unavailable, use Windows PowerShell with the same read-only action:

   ```powershell
   powershell.exe -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson
   ```

3. Present the plan: version, release channel, architecture, install root, client id, detected clients, and whether registration will be written.
4. Ask for explicit user confirmation before any mutation.
5. Use the default online installer path unless the user explicitly asks for a reviewed local archive or offline/manual package validation. The default online installer path does not need the agent to download release archives or sidecars first; the installer resolves and verifies the selected GitHub Release package.
6. For a reviewed local archive only, acquire the release package and sidecars for the selected version and architecture, then verify the archive hash, release metadata, SBOMs, and release trust policy.
7. Run the approved install path only after confirmation.

## Release artifacts to verify

For manual production review, keep these files adjacent to the archive before extraction:

- `release_<version>_win-<arch>.zip`
- `SHA256SUMS.txt`
- `release-assets.json`
- `release-sbom.spdx.json`
- `package-sbom.spdx.json`

`release-sbom.spdx.json` describes the release asset/archive inventory. `package-sbom.spdx.json` describes the package, dependency, script, assembly, and payload contents. `Signed` packages require the independent `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` trust root. Beta prereleases may use `ReleaseChecksumOnly` only when SHA256 release metadata from GitHub Release sidecars or a trusted metadata directory verifies the archive. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only an additional constraint after the thumbprint is pinned.

For the user-facing checklist, use `docfx/quickstart/manual-install.md`. For the full artifact contract, use `docfx/production/release-layout.md`.

## Approved install command shapes

Default online installer path after explicit user approval:

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

Pinned public pre-release after explicit user approval:

```powershell
$version = 'v1.0.0-beta.35'
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version $version -Prerelease
```

ARM64 archives may be published as preview assets, but they are not guaranteed stable because practical Windows-on-ARM runtime validation hardware is not currently available.

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

When a noninteractive install must prove a local package before extraction, use the reviewed local package command above so the online installer validates the ZIP against `-TrustedReleaseMetadataDirectory`. Do not use `bin\install.ps1`, `bin/install.ps1`, or `run.bat` as the noninteractive prerelease/debug trust path. `DebugTrustedRootSkip` is a development package policy, not a shortcut around archive verification.

Package-local fallback after sidecar verification:

```powershell
.\run.bat
```

Use `run.bat` from the extracted package root when the user wants the package entrypoint instead of direct PowerShell invocation.

## Agent output

Report:

- detected platform and architecture
- selected version, architecture, install root, and client id
- selected installer path: default online installer path or reviewed local package
- for a reviewed local package, release archive and sidecar paths
- hash, metadata, release SBOM, package SBOM, and release trust verification results when local sidecars are used
- installer JSON `releaseTrust.signaturePolicy` plus `releaseTrust.archiveChecksum` status, metadata source, expected SHA-256, and actual SHA-256
- exact install command approved by the user
- installed executable path and generated `client-registration` artifact path

Never print or request private keys, PFX passwords, GitHub secrets, certificate export material, auth secrets, or certificate private-key material.

## Prohibited

- Do not execute mutating remote install commands before the user approves the plan.
- Do not modify client configuration before confirmation.
- Do not use a source-tree server executable as the installed package path.
- Do not call self-signed certificates production-trusted; they are only for local development.

## Troubleshooting note

If a reviewed local script is blocked by local execution policy, inspect the script contents first and use a process-scoped policy override only from a trusted shell. Do not make policy override the normal install path.
