# 5-Minute Setup

This page is the production onboarding path for a supported MCP client on Windows.

## Requirements

- Windows.
- A supported MCP client: Claude Code, OpenAI Codex/Codex CLI, Cursor, VS Code, Visual Studio, Claude Desktop, or an artifact-only client.
- A WPF target executable path that you are allowed to inspect.
- .NET runtime requirements satisfied by the published package.

## Install

Preview pre-release installer until the first stable GitHub Release is published:

```powershell
& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease
```

The installer detects the host architecture, asks for the target MCP client, installs the packaged executable, and writes client-registration artifacts under:

```text
<InstallRoot>\<arch>\client-registration\
```

Current public onboarding uses `-Prerelease`; after the first stable GitHub Release is published, stable installs can omit that switch.

The installed server path normally resolves to:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

## Manual verified package install

Use this path when you already have a reviewed release archive.

1. Download `release_<version>_win-<arch>.zip`.
2. Keep these adjacent sidecars beside the archive: `SHA256SUMS.txt`, `release-assets.json`, `release-sbom.spdx.json`, and `package-sbom.spdx.json`.
3. Verify the archive hash against `SHA256SUMS.txt` and release metadata in `release-assets.json`.
4. Review both SBOMs: `release-sbom.spdx.json` for release assets and `package-sbom.spdx.json` for package/dependency/payload contents.
5. Verify the signer pin policy. Payload signature verification still requires `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`.
6. Extract the package and run:

   ```powershell
   .\run.bat
   ```

## Register a client

Use the generated `client-registration` artifact as the source of truth for the final command or JSON path.

| Client | Guide |
| --- | --- |
| Claude Code | [Claude Code](claude-code.md) |
| OpenAI Codex / Codex CLI | [OpenAI Codex and Codex CLI](openai-codex.md) |
| Claude Desktop | [Claude Desktop](claude-desktop.md) |
| Cursor, VS Code, Visual Studio | [Cursor, VS Code, and Visual Studio](cursor-vscode.md) |
| General client matrix | [AI Agent Clients](ai-agent-clients.md) |
| Target app owned by you | [SDK-Hosted Inspector](sdk-hosted-inspector.md) |

## Connect to a WPF target

Before the first tool call, allowlist the reviewed WPF target's exact local absolute executable path. The first scene summary also needs sensitive-read approval. Unset or malformed allowlist values fail closed before a target is attached:

```powershell
$target = 'C:\Path\To\YourApp.exe'
$env:WPFDEVTOOLS_MCP_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS = $target
$env:WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS = 'true'
```

Then start the MCP client and call:

1. `connect`
2. `get_active_process`
3. `get_ui_summary` with `depthMode: "semantic"`
4. a focused diagnostic tool recommended by the response's `navigation.recommended`

Use `get_processes(windowFilter)` only when more than one WPF target is available or when you need architecture/elevation details before connecting.

## Security defaults

The server fails closed unless the corresponding policy is explicitly enabled.

- `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` is required for every `connect()` target.
- `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` allows UI text, binding values, DependencyProperty values, event payloads, scene summaries, and runtime state reads.
- `WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true` allows `element_screenshot`.
- `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true` allows `get_viewmodel`, `get_commands`, `get_datacontext_chain`, `modify_viewmodel`, and `execute_command`; it also applies when snapshots, batch operations, or wait-after-mutation triggers request ViewModel state.
- `WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true` allows approved mutation, interaction, render measurement, and session-state-consuming tools.
- `WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS` is required before raw injection fallback can target a process.

## Troubleshooting

- If `connect()` fails, verify `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` uses exact local absolute executable paths.
- If a client cannot find the server, copy from the generated `client-registration` artifact instead of retyping the path.
- Architecture matching is mandatory for raw injection/bootstrapper fallback. SDK-hosted reuse communicates over named pipes and does not require a bitness-matched bootstrapper attach.
- If local execution policy blocks a reviewed local script, inspect the script and use a process-scoped policy override only from a trusted shell. Keep the normal path on `run.bat` or `pwsh -NoProfile -File`.
