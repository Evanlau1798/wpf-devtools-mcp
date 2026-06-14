# Releasing WPF DevTools MCP

This guide is for maintainers preparing a public release package and GitHub Release entry.
It covers local validation without uploading to GitHub, then explains how the automation takes over once you are ready.

## 1. Local preflight without uploading to GitHub

Before running packaging locally, install the native prerequisites used by the bootstrapper build:

- Visual Studio 2022 or Build Tools 2022
- `Desktop development with C++`
- MSVC `v143` build tools for `x64`, `x86`, and `ARM64`

The managed projects build with the .NET SDK, but `WpfDevTools.Bootstrapper.vcxproj` still requires the native Visual C++ toolchain for every release architecture.

For signed `Release` packaging, set the signer pin plus either a PFX path or an already-installed certificate thumbprint before running preflight:

```powershell
$env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT = '<THUMBPRINT>'
$env:WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH = '.\tmp\cert\WpfDevTools.pfx'
$env:WPFDEVTOOLS_PFX_PASSWORD = '<PFX_PASSWORD>'
```

If the signing certificate is already installed in `Cert:\CurrentUser\My`, you can omit `WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH` and let `Publish-Release.ps1` sign by thumbprint.

In CI, `WPFDEVTOOLS_PFX_PASSWORD` must be present and non-empty when `WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH` points at a PFX file. `Publish-Release.ps1` now fails fast instead of prompting.

Run the release preflight script from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/tools/packaging/Preflight-Release.ps1 -VersionTag v0.1.0 -OutputJson
```

Preflight-Release.ps1 builds, tests, packages, and optionally stages GitHub Release sidecars when `-VersionTag` is present. Use this command as the local validation gate before publishing.

To generate release zip packages locally without running the preflight validation steps or uploading anything, use:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/tools/build-release.ps1 -Configuration Release -Architectures x64,x86,arm64 -OutputRoot release
```

build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:

1. Produces `x64`, `x86`, and `arm64` release packages through `scripts/tools/packaging/Publish-Release.ps1`
2. Stops after package generation; it does not run the preflight build/test validation
3. Does not stage GitHub Release assets or upload anything

If you also want the local GitHub Release staging artifacts, run the preflight command above with `-VersionTag`. `Preflight-Release.ps1` builds, tests, produces packages, and then stages checksums, canonical upload metadata, and the release asset SBOM through `scripts/tools/packaging/Export-GitHubReleaseAssets.ps1`. The generated `release-sbom.spdx.json` is an asset-level release archive inventory, not a full package/dependency SBOM.

If the preflight reaches `x64`/`x86` successfully but fails on `ARM64`, that usually means the maintainer machine is missing the ARM64 C++ build components rather than a repository regression.

If you only want to inspect the planned commands, use:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/tools/packaging/Preflight-Release.ps1 -VersionTag v0.1.0 -PlanOnly -OutputJson
```

## 2. Verify the staged outputs

After a successful preflight run with `-VersionTag`, verify these outputs under the preflight output root:

- `packages/release_<version>_win-x64.zip`
- `packages/release_<version>_win-x86.zip`
- `packages/release_<version>_win-arm64.zip`
- `github-assets/<tag>/SHA256SUMS.txt`
- `github-assets/<tag>/release-assets.json`
- `github-assets/<tag>/release-sbom.spdx.json`
- `github-assets/<tag>/upload-gh-release.ps1`

## 2.5 Dependency audit cadence

For every release candidate, run `dotnet restore --locked-mode` before build/test and run `dotnet list package --vulnerable` after restore. Keep NuGet audit warnings as release blockers unless the release evidence records a reviewed false positive. Review `ModelContextProtocol`, `System.Text.Json`, PowerShell packaging dependencies, and GitHub Actions versions against verified advisories. Do not churn package pins for speculative CVE claims; update only for verified advisories, compatibility needs, or pinned runner/action requirements.

## 3. Release prerequisites

Before publishing a public Release channel build, confirm:

- The working tree is clean
- The version tag is final
- Release packages were produced from the tagged revision
- A self-hosted Windows ARM64 runner is available and `WPFDEVTOOLS_ENABLE_ARM64_RUNTIME_SMOKE=true` is configured for GitHub release validation
- `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` is set to the expected release signer
- `WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH` or an installed certificate thumbprint is available to `Publish-Release.ps1`
- `WPFDEVTOOLS_PFX_PASSWORD` is available when the signing certificate is supplied as a PFX file
- A public endpoint smoke check passes from an anonymous shell before any public installer docs are promoted. The repository, Releases page, latest-release API, raw installer URL, and installer alias must return HTTP 200 anonymously:
  - `https://github.com/Evanlau1798/wpf-devtools-mcp`
  - `https://github.com/Evanlau1798/wpf-devtools-mcp/releases`
  - `https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest`
  - `https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1`
  - `https://installer.wpf-mcptools.evanlau1798.com`

## 4. GitHub automation

GitHub publication is handled by `./.github/workflows/release.yml`.

Trigger modes:

- `release:` runs automatically when a GitHub Release is published
- `workflow_dispatch` lets you rerun packaging manually for a specific tag

The workflow will:

1. Check out the tagged revision
2. Materialize `WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64` into `WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH`
3. Rebuild and sign release packages for `x64`, `x86`, and `arm64` using `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`
4. Download the staged `win-arm64` asset onto a self-hosted Windows ARM64 runner, install it, launch `wpf-devtools-arm64.exe`, and validate the online-installer lane before asset upload
5. Stage checksums, metadata, and the release asset SBOM
6. Execute the generated upload helper to attach assets to the GitHub Release only after the ARM64 runtime smoke lane passes

When the workflow is triggered by `release: published`, the GitHub Release entry already exists before validation starts. If signing, packaging, upload, or ARM64 smoke validation fails, immediately retract the release entry or keep it marked as non-distributable until the workflow is rerun successfully.

The CI smoke lane in `./.github/workflows/ci-cd.yml` does not use the production certificate. Instead, it sets `WPFDEVTOOLS_INSTALLER_TEST_MODE=1`, `WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA=1`, and `WPFDEVTOOLS_TEST_SIGNATURE_STATUS=Valid` so `Publish-Release.ps1` exercises the release-signature contract deterministically on hosted runners while local archive smoke installs still trust the freshly generated release sidecars only through the explicit test hook. Hosted `windows-latest` runners continue to smoke `x64` and `x86`; the actual `arm64` executable lane runs only when `WPFDEVTOOLS_ENABLE_ARM64_RUNTIME_SMOKE=true` and the repository has a self-hosted Windows ARM64 runner.

## 5. Recommended publish sequence

1. Run the local preflight script
2. Inspect the generated zips, checksum manifest, release-assets metadata, and release asset SBOM
3. Create the Git tag locally and push it when you are satisfied
4. Create a GitHub Release or run `workflow_dispatch` against the tag
5. Verify the uploaded assets on the Release page and the online installer path

## 6. Rollback note

If a tag or GitHub Release is wrong, correct the issue locally first, then rerun the workflow for the corrected tag. Avoid patching release assets by hand.
