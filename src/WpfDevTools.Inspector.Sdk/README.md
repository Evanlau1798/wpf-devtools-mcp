# WpfDevTools.Inspector.Sdk

`WpfDevTools.Inspector.Sdk` is the Opt-in SDK for the WPF DevTools MCP Server. It enables inspection without DLL injection.

## Installation

```bash
dotnet add package WpfDevTools.Inspector.Sdk
```

## Usage

### Basic Usage

Add the following code to your WPF application's `App.xaml.cs`:

```csharp
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
- Your application uses Native AOT compilation.
- Your application is trimmed.
- Antivirus software blocks DLL injection.
- You want direct integration instead of external injection.

## How It Works

The SDK starts the Inspector host during application startup and exposes the same Named Pipe-based inspection surface that `connect()` can reuse when the MCP server and target app share matching transport settings.

## Security Coordination

The MCP server now hardens the standard injection-based transport by default. SDK mode does not receive that generated handoff automatically.

If you need hardened transport for SDK mode, set matching values for `WPFDEVTOOLS_AUTH_SECRET` and the same absolute `WPFDEVTOOLS_CERT_DIR` in both the MCP server process and the target application before calling `InspectorSdk.Initialize()`. Relative certificate paths are rejected. Without those explicit settings, the SDK helper still starts the pipe without additional transport hardening.

If you set either `WPFDEVTOOLS_AUTH_SECRET` or `WPFDEVTOOLS_CERT_DIR` for SDK mode, you must set both. Partial SDK transport configuration is rejected during `InspectorSdk.Initialize()`.

If you leave both unset, the SDK host stays plaintext. The default-hardened MCP server will not reuse that plaintext host through `connect()`.

Current requirement: `connect()` can reuse an already running SDK-hosted pipe, but only when the MCP server and target app share matching transport settings, including the same absolute `WPFDEVTOOLS_CERT_DIR` value when TLS is enabled. If the existing host responds with an incompatible authenticated or TLS handshake, `connect()` returns a security error instead of silently reusing the host; legacy plaintext or otherwise unresponsive existing hosts can still time out.

If initialization fails because the security environment variables are malformed, inspect `InspectorSdk.LastInitializationError` to retrieve the exception.

## Requirements

- .NET 8.0 or later
- Windows OS
- WPF application

## License

MIT License
