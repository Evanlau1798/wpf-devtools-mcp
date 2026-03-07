using System.Diagnostics;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Discovery;

/// <summary>
/// Detects WPF processes on the system
/// </summary>
public class WpfProcessDetector
{
    private const string WpfAssemblyName = "PresentationFramework";

    /// <summary>
    /// Get all WPF processes currently running
    /// Optimized: Filter by MainWindowHandle first to avoid expensive checks on non-GUI processes
    /// </summary>
    public IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses()
    {
        var wpfProcesses = new List<WpfProcessInfo>();
        var allProcesses = Process.GetProcesses();

        foreach (var process in allProcesses)
        {
            try
            {
                // OPTIMIZATION: Filter early by MainWindowHandle
                // Most processes don't have a window, so this eliminates ~90% immediately
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    process.Dispose();
                    continue;
                }

                var info = GetProcessInfo(process.Id);
                if (info != null && info.IsWpfApplication)
                {
                    wpfProcesses.Add(info);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to inspect process: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return wpfProcesses;
    }

    /// <summary>
    /// Get information about a specific process
    /// </summary>
    public virtual WpfProcessInfo? GetProcessInfo(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            var architecture = DetectArchitecture(process);
            var isWpf = IsWpfApplication(process);
            var dotNetVersion = DetectDotNetVersion(process);
            var windowTitle = GetMainWindowTitle(process);
            var executablePath = GetExecutablePath(process);

            var runtime = DetectRuntime(process);

            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = process.ProcessName,
                WindowTitle = windowTitle,
                Architecture = architecture,
                DotNetVersion = dotNetVersion,
                Runtime = runtime,
                IsWpfApplication = isWpf,
                ExecutablePath = executablePath
            };
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
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Error getting process info for PID {processId}: {ex.Message}");
            return null;
        }
    }

    private ProcessArchitecture DetectArchitecture(Process process)
    {
        try
        {
            // Check if process is 64-bit
            if (Environment.Is64BitOperatingSystem)
            {
                if (IsWow64Process(process.Handle, out bool isWow64))
                {
                    if (isWow64)
                    {
                        return ProcessArchitecture.X86;
                    }
                    else
                    {
                        // Could be x64 or ARM64
                        // For now, assume x64 (ARM64 detection requires more complex logic)
                        return ProcessArchitecture.X64;
                    }
                }
            }
            else
            {
                return ProcessArchitecture.X86;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to detect architecture: {ex.Message}");
        }

        return ProcessArchitecture.Unknown;
    }

    private bool IsWpfApplication(Process process)
    {
        try
        {
            // Check if PresentationFramework.dll is loaded
            foreach (ProcessModule module in process.Modules)
            {
                if (module.ModuleName?.IndexOf(WpfAssemblyName,
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            // Access denied or 64-bit process from 32-bit app - fallback to window class check
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Module enumeration failed, falling back to window class check: {ex.Message}");
            return HasWpfWindowClass(process);
        }

        return false;
    }

    private bool HasWpfWindowClass(Process process)
    {
        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                var className = GetWindowClassName(process.MainWindowHandle);
                // WPF windows typically have "HwndWrapper" class
                return className?.IndexOf("HwndWrapper", StringComparison.Ordinal) >= 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to check WPF window class: {ex.Message}");
        }

        return false;
    }

    private TargetRuntime DetectRuntime(Process process)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                var moduleName = module.ModuleName?.ToLowerInvariant();
                if (moduleName != null)
                {
                    if (moduleName.IndexOf("clr.dll", StringComparison.Ordinal) >= 0)
                        return TargetRuntime.NetFramework;
                    if (moduleName.IndexOf("coreclr.dll", StringComparison.Ordinal) >= 0)
                        return TargetRuntime.NetCore;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WpfProcessDetector: Failed to detect runtime: {ex.Message}");
        }

        return TargetRuntime.Unknown;
    }

    private string? DetectDotNetVersion(Process process)
    {
        try
        {
            // Check for .NET Framework or .NET Core/5+ runtime DLLs
            foreach (ProcessModule module in process.Modules)
            {
                var moduleName = module.ModuleName?.ToLowerInvariant();

                if (moduleName?.IndexOf("clr.dll", StringComparison.Ordinal) >= 0)
                {
                    return ".NET Framework";
                }
                else if (moduleName?.IndexOf("coreclr.dll", StringComparison.Ordinal) >= 0)
                {
                    return ".NET Core/5+";
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to detect .NET version: {ex.Message}");
        }

        return null;
    }

    private string? GetMainWindowTitle(Process process)
    {
        try
        {
            return string.IsNullOrWhiteSpace(process.MainWindowTitle)
                ? null
                : process.MainWindowTitle;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to get window title: {ex.Message}");
            return null;
        }
    }

    private string? GetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to get executable path: {ex.Message}");
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

    // P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool isWow64);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}
