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

AnyCPU inspector binaries do not remove the injector/server bitness requirement.

## missing runtime

If the server starts and exits immediately, verify the required .NET runtime is installed for the published package you downloaded.

## bootstrapper resolution

If `connect` fails after process discovery, verify the installed folder still contains the expected `bootstrapper` and `inspectors` sidecar directories next to `WpfDevTools.Mcp.Server.exe`.

## pipe readiness timeout

If injection starts but the named pipe never becomes ready, treat it as a startup readiness issue inside the target app. A blocked UI thread or a failed managed startup path can cause this state.

## Where to look next

- [Deployment Guide](../production/deployment.md)
- [Release Layout](../production/release-layout.md)
- [Bootstrap and Injection](../production/bootstrap-and-injection.md)
