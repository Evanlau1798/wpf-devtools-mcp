# Public-Path Runtime Security Checklist

Use this checklist before publishing or promoting any public installer path, GitHub Release asset, or hosted documentation update.

## Release asset integrity

- Confirm the release tag points to the reviewed commit before packaging.
- Publish only stable `x64` and `x86` archives on stable releases.
- Treat `arm64` archives as prerelease-only preview assets until a practical Windows-on-ARM runtime smoke path is available.
- Use `Signed` trust mode for stable releases, with `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` configured before publication.
- Use `ReleaseChecksumOnly` only for beta prereleases when paid signing is unavailable, and verify every archive through SHA256 release metadata.
- Ensure `SHA256SUMS.txt` is attached to the GitHub Release.
- Ensure `release-assets.json` is attached and lists every uploaded archive with size and SHA256 metadata.
- Ensure the generated GitHub Release notes include the contents of `SHA256SUMS.txt`.
- Keep `release-sbom.spdx.json`, `package-sbom.spdx.json`, and `release-evidence.json` attached for operator review.

## Public installer path

- Verify the installer alias resolves to the reviewed `scripts/online-installer.ps1` entrypoint.
- Install from the public URL or staged release metadata, not from a source checkout, for release validation.
- Verify pinned pre-release installs use `-Version <tag> -Prerelease`.
- Verify stable installs omit `-Prerelease` only after stable assets are published.
- Run `uninstall` and `full-uninstall` checks and confirm no installer-owned payloads remain.

## Runtime policy gates

- Set `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` to exact local absolute executable paths before testing `connect()`.
- Set `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` only when raw injection fallback is intentionally approved.
- Keep `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS` unset unless UI text, binding values, DP values, or runtime state may leave the target process.
- Enable screenshot, ViewModel, and destructive gates only for the specific validation session that needs them.
- Keep `WPFDEVTOOLS_AUTH_SECRET` and `WPFDEVTOOLS_CERT_DIR` consistent when validating SDK-hosted reuse.

## Mutation and restore discipline

- Use `capture_state_snapshot` before mutating or interacting with target runtime state.
- Verify the mutation with a focused read or `get_state_diff`.
- Use `restore_state_snapshot` before ending the validation run.
- Drain or document residual events when event-buffer state matters to the workflow.

## Documentation and release notes

- Keep user-facing install instructions in DocFX concise and task oriented.
- Keep contributor-only release and security procedures in the contributor section or root maintainer files.
- Do not publish E2E harness paths, temporary report names, or local agent run notes as product guidance.
- Rebuild DocFX and run documentation validation before promoting the hosted site.
