using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Inject Inspector DLL via native bootstrapper into target WPF process.
    /// Two-step injection: LoadLibraryW(bootstrapper) + CreateRemoteThread(BootstrapInspector).
    /// Verifies pipe readiness before returning success.
    /// </summary>
    InjectionResult InjectWithBootstrap(InjectionRequest request);
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

    /// <summary>
    /// Create a new ProcessInjector instance
    /// </summary>
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
        var archError = ValidateArchitecture(processId, dllPath, out var dllArch);
        if (archError != InjectionError.None)
        {
            return InjectionResult.CreateFailure(
                processId,
                archError,
                GetArchitectureErrorMessage(processId, dllArch));
        }

        // Perform injection
        return _dllInjector.Inject(processId, dllPath, actualTimeout);
    }

    /// <summary>
    /// Inject Inspector DLL via native bootstrapper.
    /// Two-step injection: LoadLibraryW(bootstrapper) + CreateRemoteThread(BootstrapInspector).
    /// Polls for Named Pipe readiness after bootstrap.
    /// </summary>
    public InjectionResult InjectWithBootstrap(InjectionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var validationError = ValidateTarget(request.ProcessId);
        if (validationError != InjectionError.None)
        {
            return InjectionResult.CreateFailure(
                request.ProcessId, validationError,
                $"Target validation failed: {validationError}");
        }

        var parameters = $"{request.InspectorDllPath};{request.ExpectedPipeName}";

        var hProcess = OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
            PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
            false, (uint)request.ProcessId);

        if (hProcess == IntPtr.Zero)
        {
            return InjectionResult.CreateFailure(
                request.ProcessId, InjectionError.AccessDenied,
                "Failed to open target process");
        }

        try
        {
            var exitCode = _dllInjector.InjectAndCallExport(
                hProcess,
                request.BootstrapperDllPath,
                "BootstrapInspector",
                parameters,
                request.InjectionTimeout);

            if (exitCode < 0)
            {
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    InjectionError.BootstrapFailed,
                    $"Injection mechanism failed at step {-exitCode}",
                    failedAtStage: BootstrapStage.LoadLibrary);
            }

            var interpretation = BootstrapResultInterpreter.Interpret(exitCode);
            if (interpretation.Error != InjectionError.None)
            {
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    interpretation.Error,
                    interpretation.Message ?? "Bootstrap failed",
                    failedAtStage: interpretation.Stage,
                    bootstrapExitCode: exitCode);
            }

            var probe = new PipeReadyProbe();
            var pipeReady = probe.WaitForPipeReady(
                request.ExpectedPipeName,
                request.PipeReadyTimeout,
                CancellationToken.None);

            if (!pipeReady)
            {
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    InjectionError.PipeReadyTimeout,
                    $"Bootstrap completed (exit code 0) but Named Pipe '{request.ExpectedPipeName}' " +
                    "did not become ready within timeout.",
                    failedAtStage: BootstrapStage.PipeReady,
                    bootstrapExitCode: exitCode);
            }

            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: exitCode,
                pipeName: request.ExpectedPipeName);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_READ = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Injector: Process {processId} validation failed: {ex.Message}");
            return InjectionError.ProcessNotFound;
        }

        return InjectionError.None;
    }

    /// <summary>
    /// Check architecture compatibility between injector, target process, and DLL.
    /// Extracted as a pure static function for testability.
    /// </summary>
    /// <param name="processArch">Target process architecture</param>
    /// <param name="dllArch">DLL architecture (Unknown = AnyCPU or undetectable)</param>
    /// <param name="isInjector64Bit">Whether the injector process is 64-bit</param>
    /// <returns>InjectionError.None if compatible, ArchitectureMismatch otherwise</returns>
    public static InjectionError CheckArchitectureCompatibility(
        ProcessArchitecture processArch, ProcessArchitecture dllArch, bool isInjector64Bit)
    {
        // Native DLL: check DLL vs target
        if (dllArch != ProcessArchitecture.Unknown &&
            processArch != ProcessArchitecture.Unknown &&
            processArch != dllArch)
        {
            return InjectionError.ArchitectureMismatch;
        }

        // Always check injector vs target (CreateRemoteThread requires same bitness)
        // This guardrail must NOT be skipped even when DLL is AnyCPU
        var injectorArch = isInjector64Bit ? ProcessArchitecture.X64 : ProcessArchitecture.X86;
        if (processArch != ProcessArchitecture.Unknown && processArch != injectorArch)
        {
            return InjectionError.ArchitectureMismatch;
        }

        return InjectionError.None;
    }

    private InjectionError ValidateArchitecture(int processId, string dllPath, out ProcessArchitecture dllArch)
    {
        dllArch = ProcessArchitecture.Unknown;

        var processInfo = _processDetector.GetProcessInfo(processId);
        if (processInfo == null)
        {
            return InjectionError.ProcessNotFound;
        }

        // Get DLL architecture using PE header reader with AnyCPU detection
        dllArch = PeArchitectureReader.Detect(dllPath);

        return CheckArchitectureCompatibility(
            processInfo.Architecture, dllArch, Environment.Is64BitProcess);
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

    /// <summary>
    /// Generate a context-aware error message for architecture mismatches.
    /// Distinguishes between DLL/target mismatch and injector/target bitness mismatch
    /// so that users (and AI agents) can take the correct recovery action.
    /// </summary>
    /// <param name="processArch">Target process architecture</param>
    /// <param name="dllArch">DLL architecture (Unknown = AnyCPU)</param>
    /// <param name="isInjector64Bit">Whether the injector/server process is 64-bit</param>
    /// <returns>Actionable error message identifying the root cause</returns>
    public static string GetArchitectureErrorMessage(
        ProcessArchitecture processArch, ProcessArchitecture dllArch, bool isInjector64Bit)
    {
        // Check if DLL itself is the problem (native DLL vs target mismatch)
        if (dllArch != ProcessArchitecture.Unknown &&
            processArch != ProcessArchitecture.Unknown &&
            processArch != dllArch)
        {
            return $"Architecture mismatch: process is {processArch}, " +
                   $"but Inspector DLL is {dllArch}. " +
                   $"Use a matching Inspector build.";
        }

        // Injector/server vs target bitness mismatch
        var injectorArch = isInjector64Bit ? ProcessArchitecture.X64 : ProcessArchitecture.X86;
        return $"Architecture mismatch: target process is {processArch}, " +
               $"but the current MCP server/injector is {injectorArch}. " +
               (dllArch == ProcessArchitecture.Unknown
                   ? "The AnyCPU Inspector DLL is not the problem; restart the MCP server with matching bitness."
                   : "Restart the MCP server with matching bitness.");
    }

    private string GetArchitectureErrorMessage(int processId, ProcessArchitecture dllArch)
    {
        var processInfo = _processDetector.GetProcessInfo(processId);
        var processArch = processInfo?.Architecture ?? ProcessArchitecture.Unknown;

        return GetArchitectureErrorMessage(processArch, dllArch, Environment.Is64BitProcess);
    }
}
