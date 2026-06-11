using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Utilities;

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
    InjectionResult InjectWithBootstrap(
        InjectionRequest request,
        CancellationToken cancellationToken = default);
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
    private readonly Func<PipeReadyProbe> _pipeReadyProbeFactory;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Create a new ProcessInjector instance
    /// </summary>
    public ProcessInjector()
        : this(new WpfProcessDetector(), new DllInjector())
    {
    }

    internal ProcessInjector(
        WpfProcessDetector processDetector,
        DllInjector dllInjector,
        Func<PipeReadyProbe>? pipeReadyProbeFactory = null)
    {
        _processDetector = processDetector ?? throw new ArgumentNullException(nameof(processDetector));
        _dllInjector = dllInjector ?? throw new ArgumentNullException(nameof(dllInjector));
        _pipeReadyProbeFactory = pipeReadyProbeFactory ?? (() => new PipeReadyProbe());
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
                InjectionError.FileNotFound,
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
    public InjectionResult InjectWithBootstrap(
        InjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        cancellationToken.ThrowIfCancellationRequested();

        var operationStopwatch = Stopwatch.StartNew();

        var validationError = ValidateTarget(request.ProcessId);
        if (validationError != InjectionError.None)
        {
            return InjectionResult.CreateFailure(
                request.ProcessId, validationError,
                $"Target validation failed: {validationError}");
        }

        var archError = ValidateArchitecture(request.ProcessId, request.BootstrapperDllPath, out var bootstrapperArch);
        if (archError != InjectionError.None)
        {
            return InjectionResult.CreateFailure(
                request.ProcessId,
                archError,
                GetArchitectureErrorMessage(request.ProcessId, bootstrapperArch, "Bootstrapper DLL"));
        }

        using var payload = request.CreateBootstrapParameterPayload();
        var parameters = payload.Parameters;

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
            var injectionTimeout = request.ResolvePhaseTimeout(
                operationStopwatch.Elapsed,
                request.InjectionTimeout);
            if (injectionTimeout <= TimeSpan.Zero)
            {
                var exhaustedSharedBudget = request.TotalTimeout.HasValue;
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    InjectionError.Timeout,
                    exhaustedSharedBudget
                        ? "Injection timed out before the bootstrap phase could start."
                        : "Injection timed out before the bootstrap phase could start because the configured injection timeout was zero or negative.",
                    failedAtStage: BootstrapStage.LoadLibrary,
                    bootstrapExitCode: null,
                    timeoutReason: exhaustedSharedBudget
                        ? InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
                        : null);
            }

            var exitCode = _dllInjector.InjectAndCallExport(
                hProcess,
                request.BootstrapperDllPath,
                "BootstrapInspector",
                parameters,
                injectionTimeout);

            if (InjectionMechanismFailure.TryInterpret(exitCode, out var mechanismFailure))
            {
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    mechanismFailure!.Error,
                    mechanismFailure!.Message,
                    failedAtStage: mechanismFailure.Stage,
                    bootstrapExitCode: exitCode,
                    timeoutReason: mechanismFailure.TimeoutReason);
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

            cancellationToken.ThrowIfCancellationRequested();

            var probe = _pipeReadyProbeFactory();
            var pipeReadyTimeout = request.ResolvePhaseTimeout(
                operationStopwatch.Elapsed,
                request.PipeReadyTimeout);
            if (pipeReadyTimeout <= TimeSpan.Zero)
            {
                var exhaustedSharedBudget = request.TotalTimeout.HasValue;
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    InjectionError.PipeReadyTimeout,
                    exhaustedSharedBudget
                        ? "Bootstrap completed, but the remaining connect budget was exhausted before the Inspector Named Pipe readiness check could start."
                        : "Bootstrap completed, but the configured pipe-ready timeout was zero or negative before the Inspector Named Pipe readiness check could start.",
                    failedAtStage: BootstrapStage.PipeReady,
                    bootstrapExitCode: exitCode,
                    timeoutReason: exhaustedSharedBudget
                        ? InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
                        : null);
            }

            var pipeReady = probe.WaitForPipeReady(
                request.ExpectedPipeName,
                pipeReadyTimeout,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

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
            System.Diagnostics.Debug.WriteLine(
                $"Injector: Process {processId} validation failed: {SensitiveLogRedactor.Redact(ex.Message)}");
            return InjectionError.ProcessNotFound;
        }

        return InjectionError.None;
    }

    /// <summary>
    /// Check architecture compatibility between injector, target process, and DLL.
    /// Extracted as a pure static function for testability.
    /// </summary>
    /// <param name="processArch">Target process architecture</param>
    /// <param name="dllArch">DLL architecture (Unknown = neutral or undetectable)</param>
    /// <param name="isInjector64Bit">Whether the injector process is 64-bit</param>
    /// <returns>InjectionError.None if compatible, ArchitectureMismatch otherwise</returns>
    public static InjectionError CheckArchitectureCompatibility(
        ProcessArchitecture processArch, ProcessArchitecture dllArch, bool isInjector64Bit)
        => CheckArchitectureCompatibility(
            processArch,
            dllArch,
            isInjector64Bit ? ProcessArchitecture.X64 : ProcessArchitecture.X86);

    /// <summary>
    /// Check architecture compatibility between injector, target process, and DLL.
    /// </summary>
    /// <param name="processArch">Target process architecture</param>
    /// <param name="dllArch">DLL architecture (Unknown = neutral or undetectable)</param>
    /// <param name="injectorArch">Current injector/server process architecture</param>
    /// <returns>InjectionError.None if compatible, ArchitectureMismatch otherwise</returns>
    public static InjectionError CheckArchitectureCompatibility(
        ProcessArchitecture processArch, ProcessArchitecture dllArch, ProcessArchitecture injectorArch)
    {
        // Native DLL: check DLL vs target
        if (dllArch != ProcessArchitecture.Unknown &&
            processArch != ProcessArchitecture.Unknown &&
            processArch != dllArch)
        {
            return InjectionError.ArchitectureMismatch;
        }

        // Always check injector vs target (CreateRemoteThread requires same bitness)
        // This guardrail must NOT be skipped even when the DLL itself is not the conflicting component
        if (processArch != ProcessArchitecture.Unknown &&
            injectorArch != ProcessArchitecture.Unknown &&
            processArch != injectorArch)
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

        // Get DLL architecture using PE header reader and neutral managed-assembly detection
        dllArch = PeArchitectureReader.Detect(dllPath);

        return CheckArchitectureCompatibility(
            processInfo.Architecture, dllArch, GetCurrentInjectorArchitecture());
    }

    private static ProcessArchitecture GetCurrentInjectorArchitecture()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => ProcessArchitecture.X86,
            Architecture.X64 => ProcessArchitecture.X64,
            Architecture.Arm64 => ProcessArchitecture.ARM64,
            _ => ProcessArchitecture.Unknown
        };

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
    /// <param name="dllArch">DLL architecture (Unknown = neutral or undetectable)</param>
    /// <param name="isInjector64Bit">Whether the injector/server process is 64-bit</param>
    /// <param name="componentName">User-facing native component name used in remediation guidance</param>
    /// <returns>Actionable error message identifying the root cause</returns>
    public static string GetArchitectureErrorMessage(
        ProcessArchitecture processArch,
        ProcessArchitecture dllArch,
        bool isInjector64Bit,
        string componentName = "Inspector DLL")
        => GetArchitectureErrorMessage(
            processArch,
            dllArch,
            isInjector64Bit ? ProcessArchitecture.X64 : ProcessArchitecture.X86,
            componentName);

    /// <summary>
    /// Generate a context-aware error message for architecture mismatches.
    /// </summary>
    public static string GetArchitectureErrorMessage(
        ProcessArchitecture processArch,
        ProcessArchitecture dllArch,
        ProcessArchitecture injectorArch,
        string componentName = "Inspector DLL")
    {
        // Check if DLL itself is the problem (native DLL vs target mismatch)
        if (dllArch != ProcessArchitecture.Unknown &&
            processArch != ProcessArchitecture.Unknown &&
            processArch != dllArch)
        {
            return $"Architecture mismatch: process is {processArch}, " +
                   $"but {componentName} is {dllArch}. " +
                   $"Use a matching {componentName} build.";
        }

        // Injector/server vs target bitness mismatch
        return $"Architecture mismatch: target process is {processArch}, " +
               $"but the current MCP server/injector is {injectorArch}. " +
               (dllArch == ProcessArchitecture.Unknown
                   ? $"The {componentName} did not report a conflicting architecture; restart the MCP server with matching bitness."
                   : "Restart the MCP server with matching bitness.");
    }

    private string GetArchitectureErrorMessage(
        int processId,
        ProcessArchitecture dllArch,
        string componentName = "Inspector DLL")
    {
        var processInfo = _processDetector.GetProcessInfo(processId);
        var processArch = processInfo?.Architecture ?? ProcessArchitecture.Unknown;

        return GetArchitectureErrorMessage(processArch, dllArch, GetCurrentInjectorArchitecture(), componentName);
    }
}
