# Releasing WPF DevTools MCP

This guide is for maintainers preparing a public release package and GitHub Release entry.
It covers local validation without uploading to GitHub, then explains how the automation takes over once you are ready.

## 1. Local preflight without uploading to GitHub

Before running packaging locally, install the native prerequisites used by the bootstrapper build:

- Visual Studio 2022 or Build Tools 2022
- `Desktop development with C++`
- MSVC `v143` build tools for `x64`, `x86`, and `ARM64`

The managed projects build with the .NET SDK, but `WpfDevTools.Bootstrapper.vcxproj` still requires the native Visual C++ toolchain for every release architecture.

Run the release preflight script from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Preflight-Release.ps1 -VersionTag v0.1.0 -OutputJson
```

What this does:

1. Builds `WpfDevTools.sln` in `Release`
2. Runs the unit test project separately with `--no-build`
3. Produces `x64`, `x86`, and `arm64` release packages through `scripts/release/Publish-Release.ps1`
4. Stages GitHub Release assets through `scripts/release/Export-GitHubReleaseAssets.ps1`
5. Stops before publication; it does not create or upload a GitHub Release

If the preflight reaches `x64`/`x86` successfully but fails on `ARM64`, that usually means the maintainer machine is missing the ARM64 C++ build components rather than a repository regression.

If you only want to inspect the planned commands, use:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Preflight-Release.ps1 -VersionTag v0.1.0 -PlanOnly -OutputJson
```

## 2. Verify the staged outputs

After a successful preflight run, verify these outputs under the preflight output root:

- `packages/WpfDevTools-win-x64.zip`
- `packages/WpfDevTools-win-x86.zip`
- `packages/WpfDevTools-win-arm64.zip`
- `github-assets/<tag>/SHA256SUMS.txt`
- `github-assets/<tag>/release-assets.json`
- `github-assets/<tag>/upload-gh-release.ps1`

## 3. Release prerequisites

Before publishing a public Release channel build, confirm:

- The working tree is clean
- The version tag is final
- Release packages were produced from the tagged revision
- Authenticode signing inputs are ready for the Inspector DLLs used in Release builds
- The GitHub Pages bootstrap installer URL still resolves to `https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1`

## 4. GitHub automation

GitHub publication is handled by `./.github/workflows/release.yml`.

Trigger modes:

- `release:` runs automatically when a GitHub Release is published
- `workflow_dispatch` lets you rerun packaging manually for a specific tag

The workflow will:

1. Check out the tagged revision
2. Rebuild release packages for `x64`, `x86`, and `arm64`
3. Stage checksums and metadata
4. Execute the generated upload helper to attach assets to the GitHub Release

## 5. Recommended publish sequence

1. Run the local preflight script
2. Inspect the generated zips and checksum manifest
3. Create the Git tag locally and push it when you are satisfied
4. Create a GitHub Release or run `workflow_dispatch` against the tag
5. Verify the uploaded assets on the Release page and the GitHub Pages installer path

## 6. Rollback note

If a tag or GitHub Release is wrong, correct the issue locally first, then rerun the workflow for the corrected tag. Avoid patching release assets by hand.
