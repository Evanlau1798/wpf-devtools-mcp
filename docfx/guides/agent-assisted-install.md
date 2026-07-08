# Agent-Assisted Install

This page is for AI agents that help a user install WPF DevTools MCP. It is intentionally shorter than a release runbook: the agent may plan, explain, and execute only after explicit user confirmation.

## Responsibilities

- Detect platform, architecture, available MCP clients, and a reusable install root.
- Present a concrete plan before any mutation.
- Verify release archive sidecars and release trust policy.
- Use the generated `client-registration` artifacts as the registration source of truth.
- Never print secrets or ask for signing material.

## Read-only planning command

For normal user setup, run the read-only plan through the public installer entrypoint:

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

The plan command reports supported clients, detected clients, architecture, default install root, and whether the step mutates files. Treat the output as evidence, not permission.

Supported client ids are `claude-code`, `codex`, `cursor`, `vscode`, `visual-studio`, `claude-desktop`, and `other`.

## Required user confirmation

Before installation, ask the user to confirm:

- version or release tag
- architecture: `x64`, `x86`, or `arm64`
- install root
- client registration target
- whether existing registration artifacts may be updated

## Release artifacts

For manual package review, keep `release_<version>_win-<arch>.zip`, `SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `package-sbom.spdx.json` together. `Signed` packages require `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`; `ReleaseChecksumOnly` beta packages require SHA256 release metadata from GitHub Release sidecars or a trusted metadata directory. Use [Manual Verified Install](../quickstart/manual-install.md) for the user-facing checklist and [Release Layout](../production/release-layout.md) for the full artifact contract.

## Install after approval

Default online installer path after explicit user approval:

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

The default online installer path does not need the agent to download release archives or sidecars first. It resolves the selected GitHub Release package, verifies release metadata, installs the packaged executable, and writes generated `client-registration` artifacts.

Pinned public pre-release after explicit user approval:

```powershell
$version = 'v1.0.0-beta.22'
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version $version -Prerelease
```

ARM64 archives may be published as preview assets, but they are not guaranteed stable because practical Windows-on-ARM runtime validation hardware is not currently available.

Reviewed local package command:

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

Use the reviewed local package command when the installer must prove a local archive before extraction. Do not use `bin\install.ps1`, `bin/install.ps1`, or `run.bat` as the noninteractive prerelease/debug trust path. `DebugTrustedRootSkip` is a development package policy, not a shortcut around archive verification.

Package-local fallback:

```powershell
.\run.bat
```

## Verify and report

After installation, report:

- installed executable path
- generated registration artifact path
- selected client id
- selected installer path: default online installer path or reviewed local package
- release sidecar verification result when local sidecars are used
- release trust mode checked
- any manual registration step still required

Do not report private keys, PFX passwords, GitHub secrets, auth secrets, or certificate private-key material.

## Troubleshooting

- If the host is not Windows, stop and report that the server supports Windows/WPF targets only.
- If sidecars are missing, do not install.
- If `Signed` package signer verification fails, stop.
- If `ReleaseChecksumOnly` prerelease SHA256 release metadata verification fails, stop.
- If client CLI discovery is blocked by elevation, use generated artifacts and ask the user to register manually.
- If `connect()` fails after install, verify `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`, process architecture, and raw-injection policy.

## Copyable prompt

```text
Read AGENT_INSTALL.md and docfx/guides/agent-assisted-install.md. Do not install yet. Run the public installer with -Action plan -OutputJson for read-only discovery. Use the default online installer path unless the user explicitly asks for a reviewed local archive. Ask for confirmation before mutation. After approval, use the stable installer alias or pinned pre-release alias; use the reviewed local package command only for local archives. Use package-local run.bat only after sidecar verification. Report generated registration artifacts and do not print secrets.
```
