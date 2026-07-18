# Manual Verified Install

Use this path only when you already have a reviewed release archive and want to install from local files. For normal onboarding, prefer the public installer on the [5-Minute Setup](index.md) page.

## Files To Download

Keep the archive and sidecars in the same directory:

| File | Required for |
| --- | --- |
| `release_<version>_win-<arch>.zip` | The package to install |
| `SHA256SUMS.txt` | Archive hash verification |
| `release-assets.json` | Release asset metadata and sidecar hashes |
| `release-sbom.spdx.json` | Release asset/archive inventory |
| `package-sbom.spdx.json` | Package, dependency, script, assembly, and payload inventory |

ARM64 archives may be published as preview assets, but they are not guaranteed stable because practical Windows-on-ARM runtime validation hardware is not currently available.

## Verify Before Install

1. Verify the ZIP hash with `SHA256SUMS.txt`.
2. Confirm the selected asset entry and sidecar hashes in `release-assets.json`.
3. Review both SBOMs so you know which release assets and package payloads are being installed.
4. Check the release trust mode:
   - `Signed` requires signer-pin verification with `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`.
   - `ReleaseChecksumOnly` is allowed only for beta prereleases when GitHub Release metadata verifies the archive SHA256.

`ReleaseChecksumOnly` protects raw injection only while exact payload bytes can still be compared with the original reviewed archive. An installed manifest alone is not a trust root. For unsigned raw injection, keep the original archive and `SHA256SUMS.txt` outside the installed payload and set `WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY` in the MCP client process; otherwise use `Signed` installed payloads.

Stop if any sidecar is missing or if the archive hash does not match.

## Install From The Reviewed Archive

Run the public installer entrypoint with the original archive and sidecar directory:

```powershell
$version = '1.0.0-beta.84'
$arch = 'x64'
$archive = (Resolve-Path ".\release_${version}_win-$arch.zip").Path
$metadata = Split-Path -Parent $archive
$installRoot = Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'

& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) `
  -Action install `
  -Architecture $arch `
  -Client other `
  -InstallRoot $installRoot `
  -PackageArchivePath $archive `
  -TrustedReleaseMetadataDirectory $metadata
```

The installed server path should resolve to:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

Register MCP clients from the generated `client-registration` directory instead of hand-writing paths.

## Portable Package Check

Use package-local `run.bat` only after sidecar verification and only when one of these is true:

- the extracted package remains next to the original archive and `SHA256SUMS.txt`
- `WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY` points to the directory containing the original archive and checksum sidecar

```powershell
.\run.bat
```

For regular MCP client setup, the installed executable path is preferred over a package-local executable.

## If Connect Fails Later

If `connect()` returns `SecurityError: Security verification failed`, first verify that the MCP client points to the installed executable under `<InstallRoot>\<arch>\current\bin\`. If you intentionally use the portable path, confirm the package can still find its original archive metadata as described above.
