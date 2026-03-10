using System.Runtime.InteropServices;
using System.Text;

namespace WpfDevTools.Injector.Discovery;

internal static class TopLevelWindowEnumerator
{
    public static IReadOnlyList<TopLevelWindowSnapshot> Enumerate()
    {
        var windows = new List<TopLevelWindowSnapshot>();

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var processId);
            windows.Add(new TopLevelWindowSnapshot(
                unchecked((int)processId),
                hWnd,
                GetWindowTextValue(hWnd),
                GetClassNameValue(hWnd),
                IsWindowVisible(hWnd)));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public static TopLevelWindowSnapshot? SelectBestWindow(
        IEnumerable<TopLevelWindowSnapshot> windows,
        int processId)
    {
        return windows
            .Where(window => window.ProcessId == processId)
            .OrderByDescending(window => window.IsVisible)
            .ThenByDescending(window => !string.IsNullOrWhiteSpace(window.Title))
            .ThenByDescending(window => window.ClassName?.IndexOf("HwndWrapper", StringComparison.Ordinal) >= 0)
            .FirstOrDefault();
    }

    private static string? GetWindowTextValue(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return null;
        }

        var builder = new StringBuilder(length + 1);
        return GetWindowText(hWnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : null;
    }

    private static string? GetClassNameValue(IntPtr hWnd)
    {
        var builder = new StringBuilder(256);
        return GetClassName(hWnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : null;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
