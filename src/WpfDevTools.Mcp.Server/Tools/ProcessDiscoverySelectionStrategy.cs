namespace WpfDevTools.Mcp.Server.Tools;

internal enum ProcessDiscoverySelectionStrategy
{
    SingleOnly = 0,
    LargestWorkingSet = 1
}

internal static class ProcessDiscoverySelectionStrategies
{
    internal static bool TryParse(string? value, out ProcessDiscoverySelectionStrategy strategy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            strategy = ProcessDiscoverySelectionStrategy.SingleOnly;
            return true;
        }

        if (string.Equals(value, "single_only", StringComparison.OrdinalIgnoreCase))
        {
            strategy = ProcessDiscoverySelectionStrategy.SingleOnly;
            return true;
        }

        if (string.Equals(value, "largest_working_set", StringComparison.OrdinalIgnoreCase))
        {
            strategy = ProcessDiscoverySelectionStrategy.LargestWorkingSet;
            return true;
        }

        strategy = ProcessDiscoverySelectionStrategy.SingleOnly;
        return false;
    }

    internal static string ToContractValue(ProcessDiscoverySelectionStrategy strategy)
        => strategy == ProcessDiscoverySelectionStrategy.LargestWorkingSet
            ? "largest_working_set"
            : "single_only";
}
