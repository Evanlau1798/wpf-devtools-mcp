# WpfDevTools.Inspector.Sdk

Opt-in SDK for WPF DevTools MCP Server - enables inspection without DLL injection.

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

        // Initialize WpfDevTools Inspector SDK
        InspectorSdk.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Shutdown WpfDevTools Inspector SDK
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

Use SDK mode instead of DLL injection when:

- Your application is a self-contained single-file app
- Your application uses Native AOT compilation
- Your application is trimmed
- Antivirus software blocks DLL injection
- You want to enable inspection in production builds

## How It Works

The SDK initializes the Inspector host on application startup, creating a Named Pipe server that the MCP Server can connect to. This provides the same inspection capabilities as DLL injection, but without requiring external process manipulation.

## Requirements

- .NET 8.0 or later
- Windows OS
- WPF application

## License

MIT License
