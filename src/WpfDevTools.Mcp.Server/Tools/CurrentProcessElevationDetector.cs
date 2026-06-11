using System.Diagnostics;
using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class CurrentProcessElevationDetector
{
    private static readonly int CurrentProcessId = ResolveCurrentProcessId();

    internal static bool IsCurrentProcessElevated()
    {
        return ProcessElevationDetector.TryIsProcessElevated(
            CurrentProcessId,
            out var isElevated)
            && isElevated;
    }

    private static int ResolveCurrentProcessId()
    {
        using var process = Process.GetCurrentProcess();
        return process.Id;
    }
}
