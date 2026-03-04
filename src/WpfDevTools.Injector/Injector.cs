using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector;

/// <summary>
/// Interface for process injection operations
/// </summary>
public interface IProcessInjector
{
    /// <summary>
    /// Inject Inspector DLL into target WPF process
    /// </summary>
    InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null);

    /// <summary>
    /// Validate target process
    /// </summary>
    InjectionError ValidateTarget(int processId);
}

/// <summary>
/// High-level injector that validates target and performs injection
/// Excluded from code coverage: requires real process injection
/// </summary>
[ExcludeFromCodeCoverage]
public class ProcessInjector : IProcessInjector
{
    private readonly WpfProcessDetector _processDetector;
    private readonly DllInjector _dllInjector;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public ProcessInjector()
    {
        _processDetector = new WpfProcessDetector();
        _dllInjector = new DllInjector();
    }

    /// <summary>
    /// Inject Inspector DLL into target WPF process
    /// </summary>
    public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
    {
        if (dllPath == null)
            throw new ArgumentNullException(nameof(dllPath));

        var actualTimeout = timeout ?? DefaultTimeout;

        // Validate target process
        var validationError = ValidateTarget(processId);
        if (validationError != InjectionError.None)
        {
            return InjectionResult.CreateFailure(
                processId,
                validationError,
                GetValidationErrorMessage(validationError, processId));
        }

        // Validate DLL path
        if (!File.Exists(dllPath))
        {
            return InjectionResult.CreateFailure(
                processId,
                InjectionError.AllocationFailed,
                $"DLL not found: {Path.GetFileName(dllPath)}");
        }

        // Check architecture compatibility
        var archError = ValidateArchitecture(processId, dllPath);
        if (archError != InjectionError.None)
        {
            return InjectionResult.CreateFailure(
                processId,
                archError,
                GetArchitectureErrorMessage(processId));
        }

        // Perform injection
        return _dllInjector.Inject(processId, dllPath, actualTimeout);
    }

    /// <summary>
    /// Validate target process
    /// </summary>
    public InjectionError ValidateTarget(int processId)
    {
        // Check if process exists
        var processInfo = _processDetector.GetProcessInfo(processId);
        if (processInfo == null)
        {
            return InjectionError.ProcessNotFound;
        }

        // Check if it's a WPF application
        if (!processInfo.IsWpfApplication)
        {
            return InjectionError.NotWpfApplication;
        }

        // Check if process is still running
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return InjectionError.ProcessNotFound;
            }
        }
        catch
        {
            return InjectionError.ProcessNotFound;
        }

        return InjectionError.None;
    }

    private InjectionError ValidateArchitecture(int processId, string dllPath)
    {
        var processInfo = _processDetector.GetProcessInfo(processId);
        if (processInfo == null)
        {
            return InjectionError.ProcessNotFound;
        }

        // Get DLL architecture
        var dllArch = GetDllArchitecture(dllPath);
        if (dllArch == ProcessArchitecture.Unknown)
        {
            // Cannot determine DLL architecture, proceed anyway
            return InjectionError.None;
        }

        // Check if architectures match
        if (processInfo.Architecture != dllArch &&
            processInfo.Architecture != ProcessArchitecture.Unknown)
        {
            return InjectionError.ArchitectureMismatch;
        }

        return InjectionError.None;
    }

    private ProcessArchitecture GetDllArchitecture(string dllPath)
    {
        try
        {
            // Simple heuristic: check if DLL path contains architecture indicators
            var lowerPath = dllPath.ToLowerInvariant();

            if (lowerPath.Contains("x64") || lowerPath.Contains("win-x64"))
            {
                return ProcessArchitecture.X64;
            }
            else if (lowerPath.Contains("x86") || lowerPath.Contains("win-x86"))
            {
                return ProcessArchitecture.X86;
            }
            else if (lowerPath.Contains("arm64") || lowerPath.Contains("win-arm64"))
            {
                return ProcessArchitecture.ARM64;
            }

            // Default: assume same as current process
            return Environment.Is64BitProcess
                ? ProcessArchitecture.X64
                : ProcessArchitecture.X86;
        }
        catch
        {
            return ProcessArchitecture.Unknown;
        }
    }

    private string GetValidationErrorMessage(InjectionError error, int processId)
    {
        return error switch
        {
            InjectionError.ProcessNotFound =>
                $"Process {processId} not found or has exited",
            InjectionError.NotWpfApplication =>
                $"Process {processId} is not a WPF application",
            InjectionError.AccessDenied =>
                $"Access denied to process {processId}. Try running as administrator.",
            _ => $"Validation failed: {error}"
        };
    }

    private string GetArchitectureErrorMessage(int processId)
    {
        var processInfo = _processDetector.GetProcessInfo(processId);
        var processArch = processInfo?.Architecture ?? ProcessArchitecture.Unknown;
        var currentArch = Environment.Is64BitProcess
            ? ProcessArchitecture.X64
            : ProcessArchitecture.X86;

        return $"Architecture mismatch: Process is {processArch}, " +
               $"but injector is {currentArch}. " +
               $"Use matching architecture build.";
    }
}
