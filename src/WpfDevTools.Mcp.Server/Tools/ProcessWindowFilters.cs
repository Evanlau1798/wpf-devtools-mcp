using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class ProcessWindowFilters
{
    internal static bool TryParse(string? value, out ProcessWindowFilter windowFilter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            windowFilter = ProcessWindowFilter.Visible;
            return true;
        }

        if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            windowFilter = ProcessWindowFilter.All;
            return true;
        }

        if (string.Equals(value, "visible", StringComparison.OrdinalIgnoreCase))
        {
            windowFilter = ProcessWindowFilter.Visible;
            return true;
        }

        if (string.Equals(value, "foreground", StringComparison.OrdinalIgnoreCase))
        {
            windowFilter = ProcessWindowFilter.Foreground;
            return true;
        }

        windowFilter = ProcessWindowFilter.Visible;
        return false;
    }
}
