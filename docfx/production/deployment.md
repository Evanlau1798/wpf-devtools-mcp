# Deployment Guide

## Deployment model

This server is usually deployed as a local Windows companion process next to the target WPF application.

When an AI agent assists the install, follow [Agent-Assisted Install](../guides/agent-assisted-install.md) so discovery, confirmation, provenance verification, and client registration stay separated.

## Canonical script sources

Installer and packaging behavior are defined in `scripts/`, not in the documentation site:

- `scripts/online-installer.ps1`
- `scripts/tools/packaging/Publish-Release.ps1`
- `scripts/installer/Installer.Actions.ps1`

## Recommended install modes

### Reviewed local package install

> **Public endpoint status:** Public release endpoints are not yet anonymously reachable. Until the GitHub repository, Releases page, latest-release API, raw installer URL, and installer alias all pass anonymous smoke checks, use a locally generated release package or a source checkout instead of remote one-line install commands.

Review `scripts/online-installer.ps1` as the maintainer source first. The reviewed installer can install a local package archive, validates archive integrity before extraction, and then installs the extracted packaged payload through the reviewed installer/helper flow.

Recommended local package example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -NonInteractive -Force -OutputJson
```

The default interactive flow asks for the release version, uses the current machine architecture (`x64`, `x86`, or `arm64`) as the default architecture, and then asks which MCP client registration to generate. When you omit `-Architecture`, the installer detects the system architecture; pass `-Architecture` only when you intentionally need to install a different package.

Client-specific automation example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-code -NonInteractive -Force -OutputJson
```

### Public release package fallback

1. Use a locally generated package, or after public endpoint smoke checks pass, download the architecture-matched `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) together with `SHA256SUMS.txt`, `release-assets.json`, and `release-sbom.spdx.json`.
2. Verify the archive with `SHA256SUMS.txt` and `release-assets.json`, then retain `release-sbom.spdx.json` as the published package SBOM before extraction.
3. Extract the package.
4. Run `run.bat`.

Before trusting the extracted package, keep the verified release sidecars beside the archive: `SHA256SUMS.txt` for the checksum, `release-assets.json` for the canonical release metadata, and `release-sbom.spdx.json` for the package SBOM. If the verified archive and those sidecars are no longer adjacent to the extracted package, set `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` as the required thumbprint trust root before launching `run.bat`; `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only an additional constraint after the thumbprint is pinned.

`run.bat` requests elevation when the current shell is not already elevated and then launches the packaged `bin/install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.

## Remote script execution is optional

Any package-local bootstrap flow is optional. Review the repository source first and treat the script in `scripts/` as the authoritative implementation.

## Release layout matters

The bootstrapper and inspector sidecars are discovered relative to the server, so the documented release layout must stay stable across installs and upgrades.

See [Release Layout](release-layout.md) for the exact folder contract.

## Server Native AOT and trimming boundary

The current packaged server is a framework-dependent, non-AOT Windows process. It is not published as a Native AOT, trimmed, or single-file server distribution.

This is intentional for the current release line because `src/WpfDevTools.Mcp.Server/Program.cs` registers MCP tools, prompts, and resources through assembly-based discovery:

- `WithToolsFromAssembly`
- `WithPromptsFromAssembly`
- `WithResourcesFromAssembly`

The MCP C# SDK marks the non-generic assembly discovery APIs with `RequiresUnreferencedCode` because they use dynamic lookup of method metadata and may not work in Native AOT or aggressively trimmed deployments. If Native AOT, trimming, or single-file server distribution becomes a product goal, first replace these registrations with AOT-safe explicit/generic registrations, then add publish-time tests that prove `tools/list`, prompts, resources, and response schemas survive trimming.

## Installed executable contract

The MCP client should launch the resolved installed `wpf-devtools-<arch>.exe` under the chosen install root, for example:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

If you do not pass `-InstallRoot`, the installer first reuses the last live install root when possible. Only when no reusable install root exists does it fall back to `%APPDATA%\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe`.

## Signed payload provenance checklist

- Keep the verified `SHA256SUMS.txt`, `release-assets.json`, and `release-sbom.spdx.json` beside the downloaded archive until after installation evidence is captured.
- Verify the signed release payloads, including `bin/wpf-devtools-<arch>.exe`, `bin/inspectors`, and `bin/bootstrapper`, against the pinned release signer. Set `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` when the verified sidecars are no longer adjacent to the extracted package.
- Run a package-local smoke check from the extracted package with `run.bat`, then verify `get_processes`, `connect`, and `get_ui_summary` before registering that install with user tools.
- Repeat the smoke check from the final installed path by launching `<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe`.

## Production checklist

- Use the architecture that matches the target process.
- Keep the published `bin/inspectors` and `bin/bootstrapper` folders next to the installed server layout.
- Sign Release inspector binaries.
- Configure authentication and TLS settings for hardened environments.
- Validate `get_processes`, `connect`, and a scene-level call such as `get_ui_summary` from the installed path outside the repository.
