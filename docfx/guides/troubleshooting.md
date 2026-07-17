# Troubleshooting

## `connect` fails immediately

Check these first:

- the target process is a running WPF application
- for raw injection/bootstrapper fallback, the server package matches the target process architecture
- for SDK-hosted reuse, the target-side Inspector host is already running with matching transport settings

## architecture mismatch

Architecture matching is mandatory for raw injection/bootstrapper fallback. The most common raw-injection fix is to switch to a package whose server and bootstrapper bitness match the target process.

- x64 target -> x64 package
- x86 target -> x86 package
- arm64 target -> arm64 package

SDK-hosted reuse communicates over named pipes and does not require matching process bitness once the target-side host is already running. The installed server, bootstrapper, and inspector sidecar still need to come from the matching package/build when the server must inject the host itself.

## missing runtime

If the server starts and exits immediately, verify the required .NET runtime is installed for the published package you downloaded.

## bootstrapper resolution

If `connect` fails after process discovery, verify the installed folder still contains the expected `bootstrapper` and `inspectors` sidecar directories next to the resolved `wpf-devtools-<arch>.exe`.

## release trust verification failure

If `connect()` returns `SecurityError: Security verification failed` after a manual package setup, check the server path first. The MCP client should point to the installed executable:

```text
<InstallRoot>\<arch>\current\bin\wpf-devtools-<arch>.exe
```

Prefer the installed executable for normal `Signed` client registration. For checksum-only prerelease raw injection, package-local or installed payloads must remain comparable with the original archive: keep the archive and `SHA256SUMS.txt` outside the installed payload, then set `WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY` in the MCP client process. The installer parameters validate the archive during installation but do not make an installed manifest a later trust root.

## elevated target or administrator mismatch

If the target WPF app is running as administrator or otherwise elevated, a non-elevated MCP host can usually discover the process but cannot control it. This typically surfaces as `Access denied` during `connect`, injection, or follow-up tool calls.

Use one of these fixes:

- restart Claude Code, Codex, or the MCP host as administrator so the server and target run at the same integrity level
- retest against a non-elevated target process
- if packaging blocks injection, start the target-side SDK host in SDK mode with `InspectorSdk.Initialize()` before calling `connect()`, and use matching transport settings including the same local absolute `WPFDEVTOOLS_CERT_DIR` value when TLS is enabled. Network paths are not allowed
- if `connect()` returns `CompatibilityError`, restart the target process so the MCP server can inject or reuse an Inspector host built from the same repo revision and compatibility contract
- a legacy plaintext or otherwise unresponsive existing SDK host may still time out before the MCP server can prove a transport mismatch

## pipe readiness timeout

If injection starts but the named pipe never becomes ready, treat it as a startup readiness issue inside the target app. A blocked UI thread or a failed managed startup path can cause this state.

## project-scoped registration confusion

If Claude Code cannot rediscover the server reliably, prefer the generated `client-registration/claude-code.txt` artifact or a project-scoped registration command. Project-scoped configuration reduces drift between shells, repos, and local profile state.

## unsupported packaging or injection limits

If the target uses self-contained single-file packaging, the standard injector path is not available. Prefer a supported desktop packaging model, or start the SDK host inside the target app with `InspectorSdk.Initialize()` before calling `connect()`. `connect()` always attempts to reuse an already running SDK host, and sidecar-free executable layouts receive the longest reuse wait.

Trimmed deployments remain risky because publishing can remove required WPF or Inspector types; SDK-host reuse is the preferred fallback, not a guarantee. Native AOT targets are not supported, and SDK-hosted reuse is not a Native AOT workaround.

## Where to look next

- [Deployment Guide](../production/deployment.md)
- [Release Layout](../production/release-layout.md)
- [Bootstrap and Injection](../production/bootstrap-and-injection.md)
