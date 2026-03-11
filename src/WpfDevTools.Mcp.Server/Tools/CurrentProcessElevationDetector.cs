using System.Diagnostics;
using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class CurrentProcessElevationDetector
{
    internal static bool IsCurrentProcessElevated()
    {
        return ProcessElevationDetector.TryIsProcessElevated(
            Process.GetCurrentProcess().Id,
            out var isElevated)
            && isElevated;
    }
}
