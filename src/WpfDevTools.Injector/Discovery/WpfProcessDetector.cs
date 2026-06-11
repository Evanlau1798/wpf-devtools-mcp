using System.Diagnostics;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Injector.Discovery;

/// <summary>
/// Detects WPF processes on the system
/// </summary>
public partial class WpfProcessDetector
{
    private const string WpfAssemblyName = "PresentationFramework";

    /// <summary>
    /// Get all WPF processes currently running
    /// Optimized: use top-level window indexing to skip non-windowed processes and
    /// avoid expensive metadata probes until the target is confirmed as WPF.
    /// </summary>
    public virtual IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses()
        => GetAllWpfProcesses(ProcessWindowFilter.Visible);

    /// <summary>
    /// Get all WPF processes currently running using the requested window visibility filter.
    /// </summary>
    public virtual IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
    {
        var wpfProcesses = new List<WpfProcessInfo>();
        var windows = TopLevelWindowEnumerator.Enumerate()
            .Where(window => MatchesWindowFilter(window, windowFilter))
            .ToArray();
        var bestWindows = BuildBestWindowIndex(windows);
        var allProcesses = Process.GetProcesses();

        foreach (var process in allProcesses)
        {
            try
            {
                if (!bestWindows.TryGetValue(process.Id, out var window))
                {
                    process.Dispose();
                    continue;
                }

                var info = CreateEnumeratedWpfProcessInfo(process, process.Id, window);
                if (info != null)
                {
                    wpfProcesses.Add(info);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"WpfProcessDetector: Failed to inspect process: {SensitiveLogRedactor.Redact(ex.Message)}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return wpfProcesses;
    }

    internal static bool MatchesWindowFilter(TopLevelWindowSnapshot window, ProcessWindowFilter windowFilter)
    {
        return windowFilter switch
        {
            ProcessWindowFilter.All => true,
            ProcessWindowFilter.Visible => window.IsVisible && !window.IsMinimized && !window.IsCloaked,
            ProcessWindowFilter.Foreground => window.IsForeground && window.IsVisible && !window.IsMinimized && !window.IsCloaked,
            _ => true
        };
    }

    /// <summary>
    /// Get information about a specific process
    /// </summary>
    public virtual WpfProcessInfo? GetProcessInfo(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var mainWindowHandle = process.MainWindowHandle;
            var mainWindowTitle = process.MainWindowTitle;
            var window = ShouldEnumerateWindowsForProcessInfo(mainWindowHandle, mainWindowTitle)
                ? TopLevelWindowEnumerator.SelectBestWindow(
                    TopLevelWindowEnumerator.Enumerate(),
                    processId)
                : null;
            return CreateProcessInfo(process, processId, window);
        }
        catch (ArgumentException)
        {
            // Process not found
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process has exited
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WpfProcessDetector: Error getting process info for PID {processId}: {SensitiveLogRedactor.Redact(ex.Message)}");
            return null;
        }
    }

    private WpfProcessInfo? CreateEnumeratedWpfProcessInfo(
        Process process,
        int processId,
        TopLevelWindowSnapshot window)
    {
        var shouldInspectModules = ShouldInspectModules(window);
        var moduleNames = shouldInspectModules ? TryGetModuleNames(process) : null;
        var isWpf = moduleNames != null
            ? ContainsWpfAssembly(moduleNames)
            : HasWpfWindowClass(process, window);

        if (!isWpf)
        {
            return null;
        }

        return CreateProcessInfo(
            process,
            processId,
            window,
            isWpfApplication: true,
            moduleNames);
    }

    private WpfProcessInfo? CreateProcessInfo(
        Process process,
        int processId,
        TopLevelWindowSnapshot? window,
        bool? isWpfApplication = null,
        string[]? moduleNames = null)
    {
        var architecture = DetectArchitecture(process);
        moduleNames ??= ShouldInspectModulesForProcessInfo(
            window,
            process.MainWindowHandle,
            process.MainWindowTitle)
            ? TryGetModuleNames(process)
            : null;
        var isWpf = isWpfApplication ?? (moduleNames != null
            ? ContainsWpfAssembly(moduleNames)
            : HasWpfWindowClass(process, window));
        var runtime = moduleNames != null
            ? DetectRuntimeFromModuleNames(moduleNames)
            : TargetRuntime.Unknown;
        var dotNetVersion = moduleNames != null
            ? DetectDotNetVersionFromModuleNames(moduleNames)
            : null;
        var isElevated = ProcessElevationDetector.TryIsProcessElevated(processId, out var elevated) && elevated;
        var titles = SelectWindowTitles(process.MainWindowTitle, window?.Title);

        return new WpfProcessInfo
        {
            ProcessId = processId,
            ProcessName = process.ProcessName,
            WindowTitle = titles.WindowTitle,
            SecondaryWindowTitle = titles.SecondaryWindowTitle,
            Architecture = architecture,
            DotNetVersion = dotNetVersion,
            Runtime = runtime,
            IsWpfApplication = isWpf,
            ExecutablePath = GetExecutablePath(process),
            IsElevated = isElevated
        };
    }

    internal static Dictionary<int, TopLevelWindowSnapshot> BuildBestWindowIndex(
        IEnumerable<TopLevelWindowSnapshot> windows)
    {
        var index = new Dictionary<int, TopLevelWindowSnapshot>();

        foreach (var window in windows)
        {
            if (!index.TryGetValue(window.ProcessId, out var existing) ||
                CompareWindowPriority(window, existing) < 0)
            {
                index[window.ProcessId] = window;
            }
        }

        return index;
    }

    internal static (string? WindowTitle, string? SecondaryWindowTitle) SelectWindowTitles(
        string? mainWindowTitle,
        string? enumeratedWindowTitle)
    {
        var preferredTitle = NormalizeWindowTitle(mainWindowTitle) ?? NormalizeWindowTitle(enumeratedWindowTitle);
        var visibleSecondaryTitle = NormalizeWindowTitle(enumeratedWindowTitle);

        if (preferredTitle == null)
        {
            return (null, null);
        }

        if (visibleSecondaryTitle == null
            || string.Equals(preferredTitle, visibleSecondaryTitle, StringComparison.Ordinal))
        {
            return (preferredTitle, null);
        }

        return (preferredTitle, visibleSecondaryTitle);
    }

    internal static bool ShouldInspectModules(TopLevelWindowSnapshot? window)
    {
        if (window == null)
        {
            return true;
        }

        var className = window.ClassName;
        if (string.IsNullOrWhiteSpace(className))
        {
            return true;
        }

        return className!.IndexOf("HwndWrapper", StringComparison.Ordinal) >= 0;
    }

    internal static bool ShouldEnumerateWindowsForProcessInfo(
        IntPtr mainWindowHandle,
        string? mainWindowTitle)
    {
        return mainWindowHandle != IntPtr.Zero ||
               !string.IsNullOrWhiteSpace(mainWindowTitle);
    }

    internal static bool ShouldInspectModulesForProcessInfo(
        TopLevelWindowSnapshot? window,
        IntPtr mainWindowHandle,
        string? mainWindowTitle)
    {
        if (window != null)
        {
            return ShouldInspectModules(window);
        }

        return ShouldEnumerateWindowsForProcessInfo(mainWindowHandle, mainWindowTitle);
    }

    private string[]? TryGetModuleNames(Process process)
    {
        try
        {
            return process.Modules
                .Cast<ProcessModule>()
                .Select(module => module.ModuleName)
                .ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WpfProcessDetector: Module enumeration failed, falling back to window class check: {SensitiveLogRedactor.Redact(ex.Message)}");
            return null;
        }
    }

    private static bool ContainsWpfAssembly(IEnumerable<string?> moduleNames)
    {
        return moduleNames.Any(moduleName =>
            moduleName?.IndexOf(WpfAssemblyName, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static int CompareWindowPriority(
        TopLevelWindowSnapshot candidate,
        TopLevelWindowSnapshot current)
    {
        var candidateRank = GetWindowPriorityRank(candidate);
        var currentRank = GetWindowPriorityRank(current);
        return candidateRank.CompareTo(currentRank);
    }

    private static int GetWindowPriorityRank(TopLevelWindowSnapshot window)
    {
        var isHwndWrapper = window.ClassName?.IndexOf("HwndWrapper", StringComparison.Ordinal) >= 0;
        return window switch
        {
            { IsVisible: true, Title: not null } when !string.IsNullOrWhiteSpace(window.Title) && isHwndWrapper => 0,
            { IsVisible: true } when isHwndWrapper => 1,
            { IsVisible: true, Title: not null } when !string.IsNullOrWhiteSpace(window.Title) => 2,
            { IsVisible: true } => 3,
            { Title: not null } when !string.IsNullOrWhiteSpace(window.Title) && isHwndWrapper => 4,
            _ when isHwndWrapper => 5,
            { Title: not null } when !string.IsNullOrWhiteSpace(window.Title) => 6,
            _ => 7
        };
    }
    private bool IsWpfApplication(Process process)
    {
        var moduleNames = TryGetModuleNames(process);
        return moduleNames != null
            ? ContainsWpfAssembly(moduleNames)
            : HasWpfWindowClass(process, window: null);
    }


    private bool HasWpfWindowClass(Process process, TopLevelWindowSnapshot? window)
    {
        try
        {
            if (window?.ClassName?.IndexOf("HwndWrapper", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                var className = GetWindowClassName(process.MainWindowHandle);
                // WPF windows typically have "HwndWrapper" class
                return className?.IndexOf("HwndWrapper", StringComparison.Ordinal) >= 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WpfProcessDetector: Failed to check WPF window class: {SensitiveLogRedactor.Redact(ex.Message)}");
        }

        return false;
    }

    private TargetRuntime DetectRuntime(Process process)
    {
        var moduleNames = TryGetModuleNames(process);
        return moduleNames != null
            ? DetectRuntimeFromModuleNames(moduleNames)
            : TargetRuntime.Unknown;
    }

    /// <summary>
    /// Detect the target runtime from loaded module file names.
    /// Exact file-name matching is used so that <c>coreclr.dll</c>
    /// is not misdiagnosed as <c>clr.dll</c>.
    /// </summary>
    public static TargetRuntime DetectRuntimeFromModuleNames(IEnumerable<string?> moduleNames)
    {
        var fileNames = moduleNames
            .Where(moduleName => !string.IsNullOrWhiteSpace(moduleName))
            .Select(moduleName => Path.GetFileName(moduleName!)?.ToLowerInvariant())
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .ToArray();

        if (fileNames.Contains("coreclr.dll", StringComparer.Ordinal))
            return TargetRuntime.NetCore;

        if (fileNames.Contains("clr.dll", StringComparer.Ordinal))
            return TargetRuntime.NetFramework;

        return TargetRuntime.Unknown;
    }

    private string? DetectDotNetVersion(Process process)
    {
        var moduleNames = TryGetModuleNames(process);
        return moduleNames != null
            ? DetectDotNetVersionFromModuleNames(moduleNames)
            : null;
    }

    private static string? DetectDotNetVersionFromModuleNames(IEnumerable<string?> moduleNames)
    {
        foreach (var moduleName in moduleNames)
        {
            var fileName = Path.GetFileName(moduleName)?.ToLowerInvariant();

            if (string.Equals(fileName, "coreclr.dll", StringComparison.Ordinal))
            {
                return ".NET Core/5+";
            }

            if (string.Equals(fileName, "clr.dll", StringComparison.Ordinal))
            {
                return ".NET Framework";
            }
        }

        return null;
    }

    private string? GetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WpfProcessDetector: Failed to get executable path: {SensitiveLogRedactor.Redact(ex.Message)}");
            return null;
        }
    }

    private string? GetWindowClassName(IntPtr hWnd)
    {
        const int maxLength = 256;
        var className = new System.Text.StringBuilder(maxLength);

        if (GetClassName(hWnd, className, maxLength) > 0)
        {
            return className.ToString();
        }

        return null;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private static string? NormalizeWindowTitle(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? null
            : title!.Trim();
}

