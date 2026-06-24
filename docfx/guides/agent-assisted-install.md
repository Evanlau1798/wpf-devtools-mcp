# Agent-Assisted Install

This page is for AI agents that help a user install WPF DevTools MCP. It is intentionally shorter than a release runbook: the agent may plan, explain, and execute only after explicit user confirmation.

## Responsibilities

- Detect platform, architecture, available MCP clients, and a reusable install root.
- Present a concrete plan before any mutation.
- Verify release archive sidecars and release trust policy.
- Use the generated `client-registration` artifacts as the registration source of truth.
- Never print secrets or ask for signing material.

## Read-only planning command

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

For manual production review, keep these files adjacent to the archive before extraction: `SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `package-sbom.spdx.json`.

`release-sbom.spdx.json` describes the release asset/archive inventory. `package-sbom.spdx.json` describes package, dependency, script, assembly, and payload contents. `Signed` packages require signer-pin verification with `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`. Beta prereleases may use `ReleaseChecksumOnly` only when SHA256 release metadata from GitHub Release sidecars or a trusted metadata directory verifies the archive.

## Install after approval

Stable release alias after explicit user approval:

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
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

Prerelease/debug trust boundary: when a noninteractive install must prove a dev/Debug package before `DebugTrustedRootSkip` is honored, use the reviewed local package command above so the online installer validates the ZIP against `-TrustedReleaseMetadataDirectory` before extraction. Do not use `bin\install.ps1`, `bin/install.ps1`, or `run.bat` as the noninteractive prerelease/debug trust path. Those package-local entrypoints are useful after separate sidecar verification, but they cannot by themselves prove which archive produced the extracted files.

Package-local fallback:

```powershell
.\run.bat
```

## Verify and report

After installation, report:

- installed executable path
- generated registration artifact path
- selected client id
- release sidecar verification result
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
Read AGENT_INSTALL.md and docfx/guides/agent-assisted-install.md. Do not install yet. Run pwsh -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson for read-only discovery, or powershell.exe -NoProfile -File with the same arguments when PowerShell 7 is unavailable. Present a plan with version, architecture, install root, client id, release archive, sidecars, and release trust policy. Ask for confirmation before mutation. After approval, use the stable installer alias, a reviewed local package command, or package-local run.bat. Report generated registration artifacts and do not print secrets.
```
