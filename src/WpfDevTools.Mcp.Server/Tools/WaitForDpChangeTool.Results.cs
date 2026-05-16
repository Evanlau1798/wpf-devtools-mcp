namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class WaitForDpChangeTool
{
    private readonly record struct DpSnapshot(string? FormattedValue, string BaseValueSource, object? Error = null)
    {
        public static DpSnapshot FromError(object error) => new(null, string.Empty, error);
    }

    private readonly record struct TriggerMutationResult(
        object? Error = null,
        bool TimedOut = false,
        bool StateAfterTimeoutUnknown = false,
        bool RequiresReconnect = false)
    {
        public static TriggerMutationResult Success => new();

        public static TriggerMutationResult Timeout(bool stateAfterTimeoutUnknown, bool requiresReconnect) =>
            new(TimedOut: true, StateAfterTimeoutUnknown: stateAfterTimeoutUnknown, RequiresReconnect: requiresReconnect);
    }
}
