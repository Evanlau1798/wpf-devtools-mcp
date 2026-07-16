# Deployment Guide

This page describes the production deployment path for a reviewed WPF DevTools MCP release package. Maintainer release qualification and sandbox automation belong in `RELEASING.md`.

## Deployment inputs

For a production review, keep the following files together:

| File | Production meaning | Requirement |
| --- | --- | --- |
| `release_<version>_win-<arch>.zip` | Versioned release package | Required |
| `SHA256SUMS.txt` | Archive checksum verification | Required |
| `release-assets.json` | Canonical release asset metadata | Required |
| `release-sbom.spdx.json` | Release asset/archive inventory | Required for release governance |
| `package-sbom.spdx.json` | Package, dependency, script, assembly, and payload SBOM | Required for full production review |

`release-sbom.spdx.json` and `package-sbom.spdx.json` are different artifacts. Sidecars prove provenance and review scope. `Signed` packages still require payload signature verification with `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`; beta prerelease packages may use `ReleaseChecksumOnly` only when the archive is verified through SHA256 release metadata.

## Install paths

Install the latest stable release:

```powershell
irm https://installer.wpf-mcptools.evanlau1798.com | iex
```

ARM64 archives may be published as preview assets, but they are not guaranteed stable because practical Windows-on-ARM runtime validation hardware is not currently available.

Reviewed local package command:

```powershell
pwsh -NoProfile -File .\scripts\online-installer.ps1 `
  -PackageArchivePath .\release\release_<version>_win-<arch>.zip `
  -TrustedReleaseMetadataDirectory .\release `
  -Client other `
  -NonInteractive -Force -OutputJson
```

Package-local fallback after sidecar verification:

```powershell
.\run.bat
```

Use `-Client other` when you want artifact-only registration output. Use a concrete client id only when the user approves writing to that client configuration.

## Trust and signer policy

1. Verify the archive hash with `SHA256SUMS.txt`.
2. Verify the asset entry and sidecar hashes in `release-assets.json`.
3. Review `release-sbom.spdx.json` for release assets.
4. Review `package-sbom.spdx.json` for package contents and dependencies.
5. For `Signed` packages, pin the expected Authenticode signer with `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`.
6. For `ReleaseChecksumOnly` beta prereleases, confirm GitHub Release notes and `release-assets.json` publish SHA256 release metadata for the selected archive.
7. Use `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` only as an additional subject constraint after the thumbprint is pinned.

## Signed payload provenance checklist

Before trusting a package for production use:

1. Verify `SHA256SUMS.txt` and `release-assets.json` against the downloaded `release_<version>_win-<arch>.zip`.
2. Confirm `release-sbom.spdx.json` is the release asset SBOM; it is not a full package/dependency SBOM.
3. Confirm `package-sbom.spdx.json` covers package, dependency, script, assembly, and payload contents.
4. Pin `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` before payload signature verification.
5. Run a package-local startup verification from the extracted archive before installing broadly.
6. Validate the final installed path points to `wpf-devtools-<arch>.exe` under the reviewed install root.

## Checksum-only prerelease checklist

Before trusting a beta prerelease package without paid signing:

1. Confirm the GitHub Release is marked prerelease.
2. Confirm the package manifest uses `ReleaseChecksumOnly`.
3. Verify the archive against SHA256 release metadata in `SHA256SUMS.txt` and `release-assets.json`.
4. Review both SBOMs before extraction.
5. Run a package-local startup verification and validate the final installed path.

## Install root and registration

If `-InstallRoot` is omitted, the installer reuses the last live install root when possible and falls back to `%APPDATA%\WpfDevToolsMcp`. The installed package layout is:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
<InstallRoot>\<arch>\client-registration\
```

Register MCP clients from the generated `client-registration` artifacts rather than hand-writing paths.

## Runtime policy

Set only the gates required for the deployment profile:

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS=<exact target exe>` is required.
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` allows sensitive UI and runtime reads.
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` allows screenshot capture.
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` allows ViewModel tools and conditional ViewModel captures.
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` allows approved mutation/interaction workflows.
- `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS=<exact target exe>` is required for raw injection fallback.

Prefer SDK-hosted Inspector reuse when you own the target app. SDK reuse requires matching `WPFDEVTOOLS_AUTH_SECRET` and the same local absolute `WPFDEVTOOLS_CERT_DIR` in both processes.

## Server distribution boundary

The packaged MCP server is a non-AOT .NET distribution. It uses assembly discovery through `WithToolsFromAssembly`, `WithPromptsFromAssembly`, and `WithResourcesFromAssembly`; the current tool/resource model is not a Native AOT contract. Treat trimming as unsupported for the server package unless a dedicated release lane proves the resulting package still preserves every reflected tool, prompt, resource, and `RequiresUnreferencedCode` boundary.

Target application packaging has separate limits. Native AOT WPF targets are not supported. Trimmed targets can make raw injection or inspector startup unreliable; prefer SDK-hosted reuse where the target app owner can validate startup.

## Dependency audit cadence

Before release promotion, run `dotnet restore --locked-mode` and `dotnet list package --vulnerable` against the locked solution. Review NuGet audit output for direct and transitive dependencies, especially `ModelContextProtocol` and `System.Text.Json`. Treat GitHub Actions dependency alerts and verified advisories as evidence to triage; do not publish speculative CVE claims without an actionable advisory or affected-version match.

## Rollback and uninstall

- Re-run the installer with the previous reviewed package to roll back.
- Use `-Action uninstall` with `-Client <client-id>` to remove or verify only the selected registration. With `-Client other`, the selected registration target is the generated `other.mcpServers.json` artifact; the installer-owned server files remain available for other clients or later reuse.
- Use `-Action full-uninstall -InstallRoot <exact-root>` to remove registrations, generated client-registration artifacts, and installer-owned server payloads only under that root. Omit `-InstallRoot` only when a test run or decommissioning workflow must remove every detected installer root.
- Remove persisted auth secrets and certificates manually only when the deployment policy requires rotation or decommissioning.

## Operational verification

After installation, verify from the final installed path:

1. Start the target WPF application.
2. Start the configured MCP client.
3. Call `connect`.
4. Call `get_active_process` and `get_ui_summary`.
5. Confirm denied gates fail closed before enabling any higher-risk capability.
