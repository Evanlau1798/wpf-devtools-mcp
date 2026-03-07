namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Structured injection request containing all parameters needed
/// for bootstrap injection. Replaces loose string parameters.
/// </summary>
public sealed class InjectionRequest
{
    /// <summary>Target process ID</summary>
    public required int ProcessId { get; init; }

    /// <summary>Path to native bootstrapper DLL (architecture-specific)</summary>
    public required string BootstrapperDllPath { get; init; }

    /// <summary>Path to managed Inspector DLL (TFM-specific)</summary>
    public required string InspectorDllPath { get; init; }

    /// <summary>Expected Named Pipe name for readiness check</summary>
    public required string ExpectedPipeName { get; init; }

    /// <summary>Timeout for the injection operation (LoadLibrary + bootstrap)</summary>
    public TimeSpan InjectionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Timeout for pipe readiness polling after bootstrap</summary>
    public TimeSpan PipeReadyTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Create the standard pipe name for a given process ID.
    /// </summary>
    public static string CreatePipeName(int processId) => $"WpfDevTools_{processId}";
}
