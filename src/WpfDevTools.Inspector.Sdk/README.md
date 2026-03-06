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

The SDK starts the Inspector host during application startup and exposes the same Named Pipe-based inspection surface that the MCP Server can use.

## Requirements

- .NET 8.0 or later
- Windows OS
- WPF application

## License

MIT License
