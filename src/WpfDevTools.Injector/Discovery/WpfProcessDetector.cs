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
    private const ushort ImageFileMachineUnknown = 0x0000;
    private const ushort ImageFileMachineI386 = 0x014c;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;

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

    private WpfProcessInfo? CreateProcessInfo(Process process, int processId)
    {
        var architecture = DetectArchitecture(process);
        var moduleNames = TryGetModuleNames(process);
        var isWpf = moduleNames != null
            ? ContainsWpfAssembly(moduleNames)
            : HasWpfWindowClass(process);
        var runtime = moduleNames != null
            ? DetectRuntimeFromModuleNames(moduleNames)
            : TargetRuntime.Unknown;
        var dotNetVersion = moduleNames != null
            ? DetectDotNetVersionFromModuleNames(moduleNames)
            : null;

        return new WpfProcessInfo
        {
            ProcessId = processId,
            ProcessName = process.ProcessName,
            WindowTitle = GetMainWindowTitle(process),
            Architecture = architecture,
            DotNetVersion = dotNetVersion,
            Runtime = runtime,
            IsWpfApplication = isWpf,
            ExecutablePath = GetExecutablePath(process)
        };
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
                $"WpfProcessDetector: Module enumeration failed, falling back to window class check: {ex.Message}");
            return null;
        }
    }

    private static bool ContainsWpfAssembly(IEnumerable<string?> moduleNames)
    {
        return moduleNames.Any(moduleName =>
            moduleName?.IndexOf(WpfAssemblyName, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private ProcessArchitecture DetectArchitecture(Process process)
    {
        try
        {
            if (!Environment.Is64BitOperatingSystem)
            {
                return ProcessArchitecture.X86;
            }

            if (TryDetectArchitectureWithIsWow64Process2(process.Handle, out var architecture))
            {
                return architecture;
            }

            if (IsWow64Process(process.Handle, out bool isWow64))
            {
                return isWow64 ? ProcessArchitecture.X86 : ProcessArchitecture.X64;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WpfProcessDetector: Failed to detect architecture: {ex.Message}");
        }

        return ProcessArchitecture.Unknown;
    }

    /// <summary>
    /// Classify a process architecture from <c>IsWow64Process2</c> machine values.
    /// Extracted as pure logic so ARM64/x64/x86 combinations can be unit tested.
    /// </summary>
    public static ProcessArchitecture DetectArchitectureFromMachineTypes(
        ushort processMachine,
        ushort nativeMachine,
        bool is64BitOperatingSystem)
    {
        if (!is64BitOperatingSystem)
        {
            return ProcessArchitecture.X86;
        }

        return processMachine switch
        {
            ImageFileMachineI386 => ProcessArchitecture.X86,
            ImageFileMachineAmd64 => ProcessArchitecture.X64,
            ImageFileMachineArm64 => ProcessArchitecture.ARM64,
            ImageFileMachineUnknown => nativeMachine switch
            {
                ImageFileMachineAmd64 => ProcessArchitecture.X64,
                ImageFileMachineArm64 => ProcessArchitecture.ARM64,
                ImageFileMachineI386 => ProcessArchitecture.X86,
                _ => ProcessArchitecture.Unknown
            },
            _ => nativeMachine switch
            {
                ImageFileMachineArm64 => ProcessArchitecture.ARM64,
                ImageFileMachineAmd64 => ProcessArchitecture.X64,
                _ => ProcessArchitecture.Unknown
            }
        };
    }

    private static bool TryDetectArchitectureWithIsWow64Process2(
        IntPtr processHandle,
        out ProcessArchitecture architecture)
    {
        architecture = ProcessArchitecture.Unknown;

        try
        {
            if (!IsWow64Process2(processHandle, out ushort processMachine, out ushort nativeMachine))
            {
                return false;
            }

            architecture = DetectArchitectureFromMachineTypes(
                processMachine,
                nativeMachine,
                Environment.Is64BitOperatingSystem);
            return architecture != ProcessArchitecture.Unknown;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    private bool IsWpfApplication(Process process)
    {
        var moduleNames = TryGetModuleNames(process);
        return moduleNames != null
            ? ContainsWpfAssembly(moduleNames)
            : HasWpfWindowClass(process);
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

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "IsWow64Process2")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(
        IntPtr hProcess,
        out ushort processMachine,
        out ushort nativeMachine);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}

