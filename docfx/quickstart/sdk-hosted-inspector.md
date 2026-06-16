# SDK-Hosted Inspector Quickstart

When you own the target application, prefer SDK-hosted reuse with `WpfDevTools.Inspector.Sdk`. When you need zero-instrumentation diagnostics for an app you cannot change, raw injection remains the fallback path.

## Package status

Until the SDK package is published to NuGet, use the repository-local package produced by the release/build pipeline. Do not copy the future NuGet install command into production onboarding until the package is actually published. For local development, create a local pack from this repository:

```powershell
dotnet pack src\WpfDevTools.Inspector.Sdk\WpfDevTools.Inspector.Sdk.csproj -c Release -o .\nupkg -p:GeneratePackageOnBuild=false
dotnet add <your-wpf-app.csproj> package WpfDevTools.Inspector.Sdk --source .\nupkg
```

The local SDK package includes the repository-internal `WpfDevTools.Inspector` and `WpfDevTools.Shared` assemblies, so consumers do not need unpublished sibling packages.

If the target app uses `PackageSourceMapping`, add an app-local `NuGet.config` entry that maps `WpfDevTools.Inspector.Sdk` to the local package source. If the app uses Central Package Management through `Directory.Packages.props`, add or override the SDK package version there instead of passing an untracked version only on the command line. Keep these restore settings in the target app repository, not in the WPF DevTools MCP checkout.

Current target framework: `net8.0-windows`. .NET Framework WPF apps should keep using the raw injection path unless and until SDK target expansion is implemented.

## Required transport settings

Set both values in the MCP server process and the target WPF application before calling `InspectorSdk.Initialize()`:

- `WPFDEVTOOLS_AUTH_SECRET`
- `WPFDEVTOOLS_CERT_DIR`

`WPFDEVTOOLS_CERT_DIR` must be a local absolute directory and must match on both sides. SDK plaintext mode is not supported by default.

Use the same values in both shells. `WPFDEVTOOLS_AUTH_SECRET` must be base64 encoded and decode to at least 32 bytes. Use 32 bytes unless your deployment policy requires longer material:

```powershell
$env:WPFDEVTOOLS_AUTH_SECRET = "base64-encoded-at-least-32-byte-secret"
$env:WPFDEVTOOLS_CERT_DIR = "C:\wpf-devtools-certs"
```

Start the MCP server from the first shell, then start the WPF target app from a second shell with the same two variables. Only call `InspectorSdk.Initialize()` after the target process has those variables in its environment.

### Expected fail-closed cases

These checks intentionally fail closed instead of falling back to plaintext SDK transport:

- A target app with missing `WPFDEVTOOLS_AUTH_SECRET` fails closed during `InspectorSdk.Initialize()` and does not start a plaintext SDK host.
- A target app with missing `WPFDEVTOOLS_CERT_DIR` fails closed during `InspectorSdk.Initialize()` and does not mix partial SDK transport settings with defaults.
- A target app with mismatched `WPFDEVTOOLS_AUTH_SECRET` starts with different HMAC material; `connect()` must reject reuse instead of accepting the host.
- A target app with mismatched `WPFDEVTOOLS_CERT_DIR` uses a different TLS certificate store; `connect()` must reject reuse because the certificate chain and thumbprint no longer match.

## Application integration

```csharp
using System.Windows;
using WpfDevTools.Inspector.Sdk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InspectorSdk.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        InspectorSdk.Shutdown();
        base.OnExit(e);
    }
}
```

If you prefer explicit process-local configuration instead of `WPFDEVTOOLS_*` environment variables in the target process, pass `InspectorSdkOptions`. Use this when the target app already reads diagnostic settings from its own config source:

```csharp
string authSecretBase64 = "...base64-encoded-at-least-32-byte-secret...";
string certificateDirectory = @"C:\absolute\wpf-devtools-certs";

InspectorSdk.InitializeWithOptions(new InspectorSdkOptions
{
    ProcessId = Environment.ProcessId,
    AuthenticationSecretBase64 = authSecretBase64,
    CertificateDirectory = certificateDirectory
});
```

Partial explicit SDK transport configuration is rejected and not mixed with environment variables. `AuthenticationSecretBase64` and `CertificateDirectory` must be provided together, and `CertificateDirectory` must be a local absolute directory. The MCP server must use the same secret and certificate directory; do not generate a new secret independently inside the target app.

After the app is running, call `connect()` from the MCP client. The server probes for a compatible SDK-hosted Inspector first and reuses it when the security settings match.

## Prefer SDK-hosted mode when

- You own the target app source code.
- You need production diagnostics without broadening raw injection policy.
- The deployment policy or AV tooling blocks DLL injection.
- The app uses single-file publish mode that makes raw injection unavailable.
- The app uses trimmed publish mode and you accept that SDK-host startup is a preferred fallback rather than a guarantee.

Native AOT targets are not supported today. SDK-hosted reuse is not a Native AOT workaround.

## Keep raw injection fallback when

- You cannot change the target app.
- You need one-off zero-instrumentation diagnostics.
- The target is a legacy app that cannot adopt the SDK quickly.
- You are debugging a field issue and need the existing bootstrapper path.

## Failure checks

- If `InspectorSdk.Initialize()` fails, inspect `InspectorSdk.LastInitializationStatus`.
- If `connect()` does not reuse the SDK host, confirm both processes share the same `WPFDEVTOOLS_AUTH_SECRET` and local absolute directory in `WPFDEVTOOLS_CERT_DIR`.
- If packaging is single-file, prefer SDK-hosted mode and treat raw injection as fallback only.
- If packaging is trimmed, prefer SDK-hosted mode but verify startup because trimming can remove required inspector types.
- Native AOT targets are not supported today; SDK-hosted reuse is not a Native AOT workaround.
