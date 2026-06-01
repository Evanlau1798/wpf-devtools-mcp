# Claude Desktop Setup

Claude Desktop uses a static JSON file, so the cleanest setup is to copy the generated JSON from the installed package output.

## 1. Install WPF DevTools

> **Public endpoint status:** Public release endpoints are not yet anonymously reachable. Until the GitHub repository, Releases page, latest-release API, raw installer URL, and installer alias all pass anonymous smoke checks, use a locally generated release package or a source checkout instead of remote one-line install commands.

Preferred local package path:

1. Review `scripts/online-installer.ps1` as the canonical source entrypoint.
2. Run the reviewed installer against a verified local package archive.

Example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\online-installer.ps1 -PackageArchivePath .\release\release_<version>_win-<arch>.zip -TrustedReleaseMetadataDirectory .\release -Client claude-desktop -NonInteractive -Force -OutputJson
```

Package-local fallback:

1. Use a locally generated package, or after public endpoint smoke checks pass, download the matching `release_<version>_win-<arch>.zip` from [Releases](https://github.com/Evanlau1798/wpf-devtools-mcp/releases) together with `SHA256SUMS.txt`, `release-assets.json`, and `release-sbom.spdx.json`.
2. Verify the archive with `SHA256SUMS.txt`, `release-assets.json`, and `release-sbom.spdx.json` before extraction.
3. Extract the package.
4. Run `run.bat`.

Before trusting the extracted package, keep the verified release sidecars beside the archive: `SHA256SUMS.txt` for the checksum, `release-assets.json` for the canonical release metadata, and `release-sbom.spdx.json` for the release asset SBOM. The SBOM sidecar is an asset-level release archive inventory, not a full package/dependency SBOM. Production payload signature verification still requires an independent `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`; adjacent sidecars prove archive provenance but do not replace signer trust. `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT` is only an additional constraint after the thumbprint is pinned.

`run.bat` requests elevation when the current shell is not already elevated and then launches the packaged `bin/install.ps1`. Set `WPFDEVTOOLS_SKIP_ELEVATION=1` when you need to keep the install in the current unelevated shell.

After installation, the fallback executable path when no previous live install root is reused is:

```text
C:\Users\<you>\AppData\Roaming\WpfDevToolsMcp\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## 2. Generated JSON template

The installer writes `client-registration\claude-desktop.json`. Its structure is:

```json
{
  "mcpServers": {
    "wpf-devtools": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Roaming\\WpfDevToolsMcp\\<arch>\\current\\bin\\wpf-devtools-<arch>.exe",
      "args": []
    }
  }
}
```

Use the generated `client-registration\claude-desktop.json` artifact as the source of truth for the resolved executable path. Only adjust the copied path in your local `claude_desktop_config.json` when you intentionally switch architectures or install roots.

Before using this prompt, confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` contains the running WPF app's exact local absolute executable path; unset or malformed values fail closed before `connect` attaches.

## 3. First prompt

```text
Use the WPF DevTools MCP server after WPFDEVTOOLS_MCP_ALLOWED_TARGETS includes the running WPF app's exact local absolute executable path; connect to it, auto-discover the target if there is only one visible candidate, then summarize the root UI state with get_ui_summary(depthMode: "semantic").
```

## Notes

- Start with `connect()` in the common case after `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` includes the reviewed target's exact local absolute executable path. Use `get_processes(windowFilter)` only when auto-discovery reports multiple candidates.
- Prefer scene-level verification before visual-tree expansion.
- After each diagnostic, interaction, or mutation, follow `navigation.recommended` first and treat `nextSteps` as the compatibility field.
- Keep mutation tools for later in the workflow.
- Reinstall or re-register after switching between `x64`, `x86`, and `arm64` targets.
