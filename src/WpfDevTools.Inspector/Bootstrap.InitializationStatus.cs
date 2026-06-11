namespace WpfDevTools.Inspector;

/// <summary>
/// Snapshot of the last bootstrap initialization outcome for target-side diagnostics.
/// </summary>
public sealed class BootstrapInitializationStatus
{
    /// <summary>
    /// Creates a bootstrap initialization status snapshot.
    /// </summary>
    public BootstrapInitializationStatus(
        string state,
        bool isInitialized,
        string? errorCode,
        string? errorType,
        string? errorMessage,
        string? hint,
        DateTimeOffset updatedUtc)
    {
        State = state;
        IsInitialized = isInitialized;
        ErrorCode = errorCode;
        ErrorType = errorType;
        ErrorMessage = errorMessage;
        Hint = hint;
        UpdatedUtc = updatedUtc;
    }

    /// <summary>
    /// High-level lifecycle state, such as NotStarted, Initialized, or Failed.
    /// </summary>
    public string State { get; }

    /// <summary>
    /// Whether the bootstrap host is currently initialized.
    /// </summary>
    public bool IsInitialized { get; }

    /// <summary>
    /// Stable failure code when initialization failed.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// CLR exception type name associated with the failure.
    /// </summary>
    public string? ErrorType { get; }

    /// <summary>
    /// Human-readable failure message.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Operator-facing remediation hint for the failure.
    /// </summary>
    public string? Hint { get; }

    /// <summary>
    /// UTC timestamp for when this snapshot was written.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; }
}

public static partial class Bootstrap
{
    /// <summary>
    /// Last structured bootstrap initialization status snapshot.
    /// </summary>
    public static BootstrapInitializationStatus LastInitializationStatus { get; private set; } = CreateNotStartedStatus();

    private static void RecordInitializationLogInfo(string message)
    {
        if (!message.StartsWith("Inspector initialized.", StringComparison.Ordinal))
        {
            return;
        }

        LastInitializationStatus = new BootstrapInitializationStatus(
            "Initialized",
            true,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    private static void RecordInitializationLogError(string message)
    {
        if (message.StartsWith("Bootstrap already", StringComparison.Ordinal))
        {
            return;
        }

        LastInitializationStatus = new BootstrapInitializationStatus(
            "Failed",
            false,
            GetBootstrapInitializationErrorCode(message),
            nameof(InvalidOperationException),
            message,
            GetBootstrapInitializationHint(message),
            DateTimeOffset.UtcNow);
    }

    private static void ResetInitializationStatusForTesting()
    {
        LastInitializationStatus = CreateNotStartedStatus();
    }

    private static BootstrapInitializationStatus CreateNotStartedStatus() => new(
        "NotStarted",
        false,
        null,
        null,
        null,
        null,
        DateTimeOffset.UtcNow);

    private static string GetBootstrapInitializationErrorCode(string message)
    {
        if (ContainsOrdinal(message, "Failed to find WPF Application instance"))
        {
            return "BootstrapDispatcherUnavailable";
        }

        if (ContainsOrdinal(message, "background scheduling failed"))
        {
            return "BootstrapBackgroundSchedulingFailed";
        }

        if (ContainsOrdinal(message, "Failed to initialize inspector"))
        {
            return "BootstrapHostInitializationFailed";
        }

        return "BootstrapInitializationFailed";
    }

    private static string GetBootstrapInitializationHint(string message)
    {
        if (string.Equals(
                GetBootstrapInitializationErrorCode(message),
                "BootstrapDispatcherUnavailable",
                StringComparison.Ordinal))
        {
            return "Ensure the target-side WPF dispatcher exists before bootstrap initialization runs.";
        }

        return "Inspect ErrorMessage and retry bootstrap initialization after correcting the target-side startup condition.";
    }

    private static bool ContainsOrdinal(string value, string expected)
        => value.IndexOf(expected, StringComparison.Ordinal) >= 0;
}
