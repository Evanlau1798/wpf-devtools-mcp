# Troubleshooting

## `connect` fails immediately

Check these first:

- the target process is a running WPF application
- the server process architecture matches the target process architecture
- the matching native bootstrapper exists in the installed release layout

## architecture mismatch

The most common fix is to switch to a package whose server and bootstrapper bitness match the target process.

- x64 target -> x64 package
- x86 target -> x86 package
- arm64 target -> arm64 package

The installed server, bootstrapper, and inspector sidecar still need to come from the package/build that matches the target bitness.

## missing runtime

If the server starts and exits immediately, verify the required .NET runtime is installed for the published package you downloaded.

## bootstrapper resolution

If `connect` fails after process discovery, verify the installed folder still contains the expected `bootstrapper` and `inspectors` sidecar directories next to the resolved `wpf-devtools-<arch>.exe`.

## elevated target or administrator mismatch

If the target WPF app is running as administrator or otherwise elevated, a non-elevated MCP host can usually discover the process but cannot control it. This typically surfaces as `Access denied` during `connect`, injection, or follow-up tool calls.

Use one of these fixes:

- restart Claude Code, Codex, or the MCP host as administrator so the server and target run at the same integrity level
- retest against a non-elevated target process
- use SDK mode when the packaging or deployment model does not support the normal injection path

## pipe readiness timeout

If injection starts but the named pipe never becomes ready, treat it as a startup readiness issue inside the target app. A blocked UI thread or a failed managed startup path can cause this state.

## project-scoped registration confusion

If Claude Code cannot rediscover the server reliably, prefer the generated `client-registration/claude-code.txt` artifact or a project-scoped registration command. Project-scoped configuration reduces drift between shells, repos, and local profile state.

## unsupported packaging or injection limits

If the target uses unsupported packaging, such as trimmed deployment, self-contained single-file distribution, or native AOT, the standard injector path may not be available. In those cases, prefer SDK mode or a supported desktop packaging model.

## Where to look next

- [Deployment Guide](../production/deployment.md)
- [Release Layout](../production/release-layout.md)
- [Bootstrap and Injection](../production/bootstrap-and-injection.md)
