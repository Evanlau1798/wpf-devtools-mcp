namespace WpfDevTools.Mcp.Server;

internal static class EnvironmentVariableDiagnostics
{
    public const string AcceptedBooleanValues =
        "Accepted values: true, 1, yes, on; false, 0, no, off.";

    public static string FormatInvalidEntryCount(int invalidEntryCount)
        => invalidEntryCount switch
        {
            1 => "1 configured entry is invalid.",
            > 1 => $"{invalidEntryCount} configured entries are invalid.",
            _ => "No valid configured entries were found."
        };
}
