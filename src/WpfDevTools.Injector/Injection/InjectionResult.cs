using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Result of DLL injection operation
/// </summary>
public sealed class InjectionResult
{
    /// <summary>
    /// Whether injection was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error type if injection failed
    /// </summary>
    public InjectionError Error { get; init; }

    /// <summary>
    /// Detailed error message
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Process ID that was injected
    /// </summary>
    public int ProcessId { get; init; }

    /// <summary>
    /// Path to the DLL that was injected
    /// </summary>
    public string? DllPath { get; init; }

    /// <summary>Bootstrap exit code from remote thread (diagnostic, not readiness)</summary>
    public int? BootstrapExitCode { get; init; }

    /// <summary>Stage at which bootstrap failed (null if successful)</summary>
    public BootstrapStage? FailedAtStage { get; init; }

    /// <summary>Named Pipe name (populated on success)</summary>
    public string? PipeName { get; init; }

    /// <summary>
    /// Create a successful result
    /// </summary>
    public static InjectionResult CreateSuccess(
        int processId, string dllPath,
        int? bootstrapExitCode = null, string? pipeName = null)
    {
        return new InjectionResult
        {
            Success = true,
            Error = InjectionError.None,
            ProcessId = processId,
            DllPath = dllPath,
            BootstrapExitCode = bootstrapExitCode,
            PipeName = pipeName
        };
    }

    /// <summary>
    /// Create a failed result
    /// </summary>
    public static InjectionResult CreateFailure(
        int processId,
        InjectionError error,
        string errorMessage,
        BootstrapStage? failedAtStage = null,
        int? bootstrapExitCode = null)
    {
        return new InjectionResult
        {
            Success = false,
            Error = error,
            ErrorMessage = errorMessage,
            ProcessId = processId,
            FailedAtStage = failedAtStage,
            BootstrapExitCode = bootstrapExitCode
        };
    }
}
