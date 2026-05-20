# SDK-Hosted Inspector Quickstart

When you own the target application, prefer SDK-hosted reuse with `WpfDevTools.Inspector.Sdk`. When you need zero-instrumentation diagnostics for an app you cannot change, raw injection remains the fallback path.

## Package status

The NuGet package is not yet publicly published. Until publication, use a local pack from this repository:

```powershell
dotnet pack src\WpfDevTools.Inspector.Sdk\WpfDevTools.Inspector.Sdk.csproj -c Release -o .\nupkg -p:GeneratePackageOnBuild=false
dotnet add <your-wpf-app.csproj> package WpfDevTools.Inspector.Sdk --source .\nupkg
```

Current target framework: `net8.0-windows`. .NET Framework WPF apps should keep using the raw injection path unless and until SDK target expansion is implemented.

## Required transport settings

Set both values in the MCP server process and the target WPF application before calling `InspectorSdk.Initialize()`:

- `WPFDEVTOOLS_AUTH_SECRET`
- `WPFDEVTOOLS_CERT_DIR`

`WPFDEVTOOLS_CERT_DIR` must be an absolute path and must match on both sides. SDK plaintext mode is not supported by default.

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
string authSecretBase64 = "...base64-encoded-32-byte-secret...";
string certificateDirectory = @"C:\absolute\wpf-devtools-certs";

InspectorSdk.Initialize(new InspectorSdkOptions
{
    ProcessId = Environment.ProcessId,
    AuthenticationSecretBase64 = authSecretBase64,
    CertificateDirectory = certificateDirectory
});
```

Partial explicit SDK transport configuration is rejected and not mixed with environment variables. `AuthenticationSecretBase64` and `CertificateDirectory` must be provided together, and `CertificateDirectory` must be absolute. The MCP server must use the same secret and certificate directory; do not generate a new secret independently inside the target app.

After the app is running, call `connect()` from the MCP client. The server probes for a compatible SDK-hosted Inspector first and reuses it when the security settings match.

## Prefer SDK-hosted mode when

- You own the target app source code.
- You need production diagnostics without broadening raw injection policy.
- The deployment policy or AV tooling blocks DLL injection.
- The app uses single-file, Native AOT, or trimmed publish modes that make raw injection unreliable.

## Keep raw injection fallback when

- You cannot change the target app.
- You need one-off zero-instrumentation diagnostics.
- The target is a legacy app that cannot adopt the SDK quickly.
- You are debugging a field issue and need the existing bootstrapper path.

## Failure checks

- If `InspectorSdk.Initialize()` fails, inspect `InspectorSdk.LastInitializationStatus`.
- If `connect()` does not reuse the SDK host, confirm both processes share the same `WPFDEVTOOLS_AUTH_SECRET` and absolute `WPFDEVTOOLS_CERT_DIR`.
- If packaging is single-file, Native AOT, or trimmed, prefer SDK-hosted mode and treat raw injection as fallback only.
