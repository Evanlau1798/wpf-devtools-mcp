# Agent-Assisted Install

This page is for AI agents that help a user install WPF DevTools MCP. It is intentionally shorter than a release runbook: the agent may plan, explain, and execute only after explicit user confirmation.

## Responsibilities

- Detect platform, architecture, available MCP clients, and a reusable install root.
- Present a concrete plan before any mutation.
- Verify release archive sidecars and signer-pin policy.
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

`release-sbom.spdx.json` describes the release asset/archive inventory. `package-sbom.spdx.json` describes package, dependency, script, assembly, and payload contents. Payload signature verification still requires `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`.

## Install after approval

Preview pre-release alias until the first stable GitHub Release is published:

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

Current public onboarding uses `-Prerelease`; after the first stable GitHub Release is published, stable installs can omit that switch.

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
- signer pin checked
- any manual registration step still required

Do not report private keys, PFX passwords, GitHub secrets, auth secrets, or certificate private-key material.

## Troubleshooting

- If the host is not Windows, stop and report that the server supports Windows/WPF targets only.
- If sidecars are missing, do not install.
- If signer verification fails, stop.
- If client CLI discovery is blocked by elevation, use generated artifacts and ask the user to register manually.
- If `connect()` fails after install, verify `WPFDEVTOOLS_MCP_ALLOWED_TARGETS`, process architecture, and raw-injection policy.

## Copyable prompt

```text
Read AGENT_INSTALL.md and docfx/guides/agent-assisted-install.md. Do not install yet. Run pwsh -NoProfile -File .\scripts\online-installer.ps1 -Action plan -OutputJson for read-only discovery, or powershell.exe -NoProfile -File with the same arguments when PowerShell 7 is unavailable. Present a plan with version, architecture, install root, client id, release archive, sidecars, and signer pin policy. Ask for confirmation before mutation. After approval, use the preview pre-release alias, a reviewed local package command, or package-local run.bat. Report generated registration artifacts and do not print secrets.
```
