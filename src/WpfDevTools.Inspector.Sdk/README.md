# WpfDevTools.Inspector.Sdk

`WpfDevTools.Inspector.Sdk` is the Opt-in SDK for the WPF DevTools MCP Server. It enables inspection without DLL injection.

When you own the target application, prefer SDK-hosted reuse; raw injection remains the fallback path for zero-instrumentation diagnostics and targets that cannot be modified.

Single-file packaging constraints affect raw injection, not the single-file WPF app support posture. When the target app starts `InspectorSdk.Initialize()` with matching transport settings, `connect()` can reuse the SDK-hosted Inspector instead of injecting a DLL.

Native AOT targets are not supported today; SDK-hosted reuse is not a Native AOT workaround. Trimmed apps are still risky because required inspector types may be removed, so SDK-host reuse is the preferred fallback rather than a guarantee.

## Installation

Before public NuGet publication, build the package locally from the repository root and install it from an explicit package source:

```bash
dotnet pack src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj --configuration Release --output ./nupkg -p:GeneratePackageOnBuild=false
dotnet add <your-wpf-app.csproj> package WpfDevTools.Inspector.Sdk --source ./nupkg
```

The local SDK package includes the repository-internal `WpfDevTools.Inspector` and `WpfDevTools.Shared` assemblies, so consumers do not need unpublished sibling packages.

After public NuGet publication, consumers can install from the configured NuGet.org source:

```bash
dotnet add <your-wpf-app.csproj> package WpfDevTools.Inspector.Sdk
```

## Usage

Before using the sample below, set matching `WPFDEVTOOLS_AUTH_SECRET` and the same local absolute directory in `WPFDEVTOOLS_CERT_DIR` in both the MCP server process and the target application environment. `InspectorSdk.Initialize()` fails closed if either value is missing or if both are left unset.

Use the same values in both shells. The auth secret must decode to exactly 32 bytes:

```powershell
$env:WPFDEVTOOLS_AUTH_SECRET = "base64-encoded-32-byte-secret"
$env:WPFDEVTOOLS_CERT_DIR = "C:\wpf-devtools-certs"
```

Start the MCP server from the first shell, then start the WPF target app from a second shell with the same two variables. Only call `InspectorSdk.Initialize()` after the target process has those variables in its environment.

### Expected fail-closed cases

These checks intentionally fail closed instead of falling back to plaintext SDK transport:

- A target app with missing `WPFDEVTOOLS_AUTH_SECRET` fails closed during `InspectorSdk.Initialize()` and does not start a plaintext SDK host.
- A target app with missing `WPFDEVTOOLS_CERT_DIR` fails closed during `InspectorSdk.Initialize()` and does not mix partial SDK transport settings with defaults.
- A target app with mismatched `WPFDEVTOOLS_AUTH_SECRET` starts with different HMAC material; `connect()` must reject reuse instead of accepting the host.
- A target app with mismatched `WPFDEVTOOLS_CERT_DIR` uses a different TLS certificate store; `connect()` must reject reuse because the certificate chain and thumbprint no longer match.

### Basic Usage

Add the following code to your WPF application's `App.xaml.cs`:

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

### Explicit Options

Use `InspectorSdkOptions` when you want target-side configuration in code instead of relying only on process environment variables. This is useful when your app reads diagnostics settings from its own config provider.

```csharp
string authSecretBase64 = "...base64-encoded-32-byte-secret...";
string certificateDirectory = @"C:\absolute\wpf-devtools-certs";

InspectorSdk.InitializeWithOptions(new InspectorSdkOptions
{
    ProcessId = Environment.ProcessId,
    AuthenticationSecretBase64 = authSecretBase64,
    CertificateDirectory = certificateDirectory
});
```

Partial explicit SDK transport configuration is rejected and not mixed with environment variables. `AuthenticationSecretBase64` and `CertificateDirectory` must be supplied together, and `CertificateDirectory` must be a local absolute directory. The MCP server must use the same secret and certificate directory, so do not generate a fresh target-only secret during app startup.

### Custom Process ID

If you need to specify a custom process ID:

```csharp
InspectorSdk.Initialize(processId: 12345);
```

### Check Initialization Status

```csharp
if (InspectorSdk.IsInitialized)
{
    Console.WriteLine("WpfDevTools Inspector is running");
}
```

## When to Use SDK Mode

Use the Opt-in SDK when:

- Your application is a self-contained single-file app.
- Your application is trimmed.
- Antivirus software blocks DLL injection.
- You want direct integration instead of external injection.

Native AOT targets are not supported today. SDK-hosted reuse is not a Native AOT workaround.

## How It Works

The SDK starts the Inspector host during application startup and exposes the same Named Pipe-based inspection surface that `connect()` can reuse when the MCP server and target app share matching transport settings.

## Security Coordination

The MCP server now hardens the standard injection-based transport by default. SDK mode does not receive that generated handoff automatically.

If you need SDK mode, set matching values for `WPFDEVTOOLS_AUTH_SECRET` and the same local absolute directory in `WPFDEVTOOLS_CERT_DIR` in both the MCP server process and the target application before calling `InspectorSdk.Initialize()`. Relative certificate paths are rejected, and the MCP server rejects network/UNC certificate directories.

If you set either `WPFDEVTOOLS_AUTH_SECRET` or `WPFDEVTOOLS_CERT_DIR` for SDK mode, you must set both. Partial SDK transport configuration is rejected during `InspectorSdk.Initialize()`.

If you leave both unset, `InspectorSdk.Initialize()` now fails closed instead of starting a plaintext SDK host. The default-hardened MCP server will not reuse a plaintext SDK host left behind by older versions.

Current requirement: `connect()` can reuse an already running SDK-hosted pipe, but only when the MCP server and target app share matching transport settings, including the same local absolute directory in `WPFDEVTOOLS_CERT_DIR` when TLS is enabled. If the existing host responds with an incompatible authenticated or TLS handshake, `connect()` returns a security error instead of silently reusing the host; legacy plaintext or otherwise unresponsive existing hosts can still time out.

If initialization fails because the security environment variables are malformed, inspect `InspectorSdk.LastInitializationError` to retrieve the exception.

## Requirements

- .NET 8.0 or later
- Windows OS
- WPF application

## License

WPF DevTools Inspector SDK is licensed under the Apache License, Version 2.0.
