using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Discovery;

/// <summary>
/// Information about a WPF process
/// </summary>
public sealed class WpfProcessInfo
{
    /// <summary>
    /// Process ID
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// Process name
    /// </summary>
    public required string ProcessName { get; init; }

    /// <summary>
    /// Main window title
    /// </summary>
    public string? WindowTitle { get; init; }

    /// <summary>
    /// Process architecture
    /// </summary>
    public required ProcessArchitecture Architecture { get; init; }

    /// <summary>
    /// .NET runtime version (e.g., ".NET Framework 4.8", ".NET 8.0")
    /// </summary>
    public string? DotNetVersion { get; init; }

    /// <summary>
    /// Target CLR runtime type (NetFramework or NetCore).
    /// Detected from loaded modules (clr.dll vs coreclr.dll).
    /// </summary>
    public TargetRuntime Runtime { get; init; }

    /// <summary>
    /// Whether this is a WPF application
    /// </summary>
    public required bool IsWpfApplication { get; init; }

    /// <summary>
    /// Full path to the executable
    /// </summary>
    public string? ExecutablePath { get; init; }
}
