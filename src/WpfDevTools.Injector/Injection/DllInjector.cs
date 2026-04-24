// Portions of this code are derived from Snoop WPF (https://github.com/snoopwpf/snoopwpf)
// Licensed under Microsoft Public License (Ms-PL)
// See LICENSE file for full Ms-PL license text

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Injects DLL into target process using CreateRemoteThread.
/// Based on Snoop's injection mechanism (Ms-PL licensed).
/// Excluded from code coverage: requires P/Invoke calls to inject into real processes.
/// </summary>
[ExcludeFromCodeCoverage]
public class DllInjector
{
    private enum LoadLibraryRemoteOutcome
    {
        Succeeded,
        Failed,
        TimedOut
    }

    private const int PROCESS_CREATE_THREAD = 0x0002;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_VM_OPERATION = 0x0008;
    private const int PROCESS_VM_WRITE = 0x0020;
    private const int PROCESS_VM_READ = 0x0010;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly IRemoteInjectionApi _api;
    private readonly RemoteThreadBufferInvoker _bufferInvoker;

    /// <summary>
    /// Create a DLL injector backed by the native Win32 injection API.
    /// </summary>
    public DllInjector()
        : this(
            Win32RemoteInjectionApi.Instance,
            DeferredRemoteAllocationCleanupScheduler.Instance)
    {
    }

    internal DllInjector(
        IRemoteInjectionApi api,
        IRemoteAllocationCleanupScheduler cleanupScheduler)
    {
        _api = api;
        _bufferInvoker = new RemoteThreadBufferInvoker(api, cleanupScheduler);
    }

    /// <summary>
    /// Inject DLL into target process
    /// </summary>
    public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
    {
        if (dllPath == null)
            throw new ArgumentNullException(nameof(dllPath));

        var actualTimeout = timeout ?? DefaultTimeout;

        // Validate injection parameters
        var validationError = ValidateInjection(processId, dllPath);
        if (validationError != InjectionError.None)
        {
            return InjectionResult.CreateFailure(
                processId,
                validationError,
                GetErrorMessage(validationError));
        }

        // Perform injection
        return PerformInjection(processId, dllPath, actualTimeout);
    }

    /// <summary>
    /// Validate injection parameters
    /// </summary>
    public InjectionError ValidateInjection(int processId, string dllPath)
    {
        // Check if process exists
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return InjectionError.ProcessNotFound;
            }
        }
        catch (ArgumentException)
        {
            return InjectionError.ProcessNotFound;
        }
        catch (InvalidOperationException)
        {
            // Process has exited or access denied
            return InjectionError.AccessDenied;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Win32 error accessing process
            return InjectionError.AccessDenied;
        }

        // Check if DLL exists
        if (!File.Exists(dllPath))
        {
            return InjectionError.AllocationFailed;
        }

        return InjectionError.None;
    }

    private InjectionResult PerformInjection(int processId, string dllPath, TimeSpan timeout)
    {
        IntPtr hProcess = IntPtr.Zero;

        try
        {
            // Open target process
            hProcess = _api.OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false,
                processId);

            if (hProcess == IntPtr.Zero || hProcess == new IntPtr(-1))
            {
                return CreateSanitizedFailure(
                    processId,
                    InjectionError.AccessDenied,
                    "Failed to open target process for injection",
                    $"OpenProcess failed with Win32 error {_api.GetLastError()}.");
            }

            // Get LoadLibraryW address
            var kernel32 = _api.GetModuleHandle("kernel32.dll");
            var loadLibraryAddr = _api.GetProcAddress(kernel32, "LoadLibraryW");

            if (loadLibraryAddr == IntPtr.Zero)
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.Unknown,
                    "Failed to get LoadLibraryW address");
            }

            // Allocate memory in target process
            var dllPathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            var invocationResult = _bufferInvoker.Invoke(
                hProcess,
                loadLibraryAddr,
                dllPathBytes,
                timeout,
                requireExitCode: false);

            return invocationResult.Status switch
            {
                RemoteThreadInvocationStatus.Completed => InjectionResult.CreateSuccess(processId, dllPath),
                RemoteThreadInvocationStatus.AllocationFailed => CreateSanitizedFailure(
                    processId,
                    InjectionError.AllocationFailed,
                    "Failed to allocate remote memory for injection",
                    $"VirtualAllocEx failed with Win32 error {invocationResult.LastError}."),
                RemoteThreadInvocationStatus.WriteFailed => CreateSanitizedFailure(
                    processId,
                    InjectionError.WriteFailed,
                    "Failed to write injector payload into target process",
                    $"WriteProcessMemory failed with Win32 error {invocationResult.LastError}."),
                RemoteThreadInvocationStatus.ThreadCreationFailed => CreateSanitizedFailure(
                    processId,
                    InjectionError.CreateThreadFailed,
                    "Failed to start remote injection thread",
                    $"CreateRemoteThread failed with Win32 error {invocationResult.LastError}."),
                RemoteThreadInvocationStatus.TimedOut => InjectionResult.CreateFailure(
                    processId,
                    InjectionError.Timeout,
                    "Injection timed out"),
                RemoteThreadInvocationStatus.WaitFailed => CreateSanitizedFailure(
                    processId,
                    InjectionError.Unknown,
                    "The injector could not confirm remote thread completion",
                    $"WaitForSingleObject failed with Win32 error {invocationResult.LastError}."),
                RemoteThreadInvocationStatus.UnexpectedWaitResult => CreateSanitizedFailure(
                    processId,
                    InjectionError.Unknown,
                    "The injector could not confirm remote thread completion",
                    $"Unexpected wait result 0x{invocationResult.WaitResult:X8}."),
                RemoteThreadInvocationStatus.DeferredCleanupSchedulingFailed => CreateSanitizedFailure(
                    processId,
                    InjectionError.Unknown,
                    "The injector could not confirm remote thread completion",
                    "Remote thread did not complete, and deferred remote buffer cleanup could not be scheduled."),
                _ => InjectionResult.CreateFailure(
                    processId,
                    InjectionError.Unknown,
                    "Injection thread completed, but the injector could not confirm its result.")
            };
        }
        catch (Exception ex)
        {
            return CreateSanitizedFailure(
                processId,
                InjectionError.Unknown,
                "Unexpected injection failure",
                ex);
        }
        finally
        {
            if (hProcess != IntPtr.Zero)
            {
                _api.CloseHandle(hProcess);
            }
        }
    }

    /// <summary>
    /// Two-step injection:
    /// Step 1: CreateRemoteThread(LoadLibraryW, bootstrapperPath) to load native DLL
    /// Step 2: Find remote BootstrapInspector address via PE export RVA + remote base,
    ///         write params to target memory, CreateRemoteThread(BootstrapInspector, params)
    /// </summary>
    /// <returns>Step 2 remote thread exit code, or negative value on failure</returns>
    public int InjectAndCallExport(
        IntPtr hProcess,
        string bootstrapperPath,
        string exportName,
        string parameters,
        TimeSpan timeout)
    {
        var operationStopwatch = Stopwatch.StartNew();

        // Step 1: Load bootstrapper via LoadLibraryW
        var loadLibraryTimeout = GetRemainingBootstrapPhaseTimeout(operationStopwatch.Elapsed, timeout);
        if (loadLibraryTimeout <= TimeSpan.Zero)
            return InjectionMechanismFailure.LoadBootstrapperBudgetExhausted;

        var loadLibraryOutcome = LoadLibraryRemote(hProcess, bootstrapperPath, loadLibraryTimeout);
        if (loadLibraryOutcome == LoadLibraryRemoteOutcome.TimedOut)
            return InjectionMechanismFailure.LoadBootstrapperTimedOut;
        if (loadLibraryOutcome != LoadLibraryRemoteOutcome.Succeeded)
            return InjectionMechanismFailure.LoadBootstrapperFailed;

        // Find remote module base
        var remoteBase = RemoteModuleResolver.FindModuleBase(hProcess, bootstrapperPath);
        if (remoteBase == IntPtr.Zero)
            return InjectionMechanismFailure.ResolveRemoteModuleFailed;

        // Read local PE to get export RVA
        var exportRva = PeExportReader.GetExportRva(bootstrapperPath, exportName);
        if (exportRva == null)
            return InjectionMechanismFailure.ResolveBootstrapExportFailed;

        // Compute remote function address
        var remoteFuncAddr = IntPtr.Add(remoteBase, (int)exportRva.Value);

        // Write parameters to target process memory
        var paramBytes = Encoding.Unicode.GetBytes(parameters + "\0");
        var invokeExportTimeout = GetRemainingBootstrapPhaseTimeout(operationStopwatch.Elapsed, timeout);
        if (invokeExportTimeout <= TimeSpan.Zero)
            return InjectionMechanismFailure.InvokeBootstrapExportBudgetExhausted;

        var invocationResult = _bufferInvoker.Invoke(
            hProcess,
            remoteFuncAddr,
            paramBytes,
            invokeExportTimeout,
            requireExitCode: true);

        return invocationResult.Status switch
        {
            RemoteThreadInvocationStatus.Completed => (int)invocationResult.ExitCode,
            RemoteThreadInvocationStatus.AllocationFailed =>
                InjectionMechanismFailure.AllocateBootstrapParametersFailed,
            RemoteThreadInvocationStatus.WriteFailed =>
                InjectionMechanismFailure.WriteBootstrapParametersFailed,
            RemoteThreadInvocationStatus.ThreadCreationFailed =>
                InjectionMechanismFailure.StartBootstrapExportFailed,
            RemoteThreadInvocationStatus.ExitCodeUnavailable =>
                InjectionMechanismFailure.ReadBootstrapExitCodeFailed,
            RemoteThreadInvocationStatus.DeferredCleanupSchedulingFailed =>
                InjectionMechanismFailure.ScheduleBootstrapCleanupFailed,
            _ => InjectionMechanismFailure.InvokeBootstrapExportTimedOut
        };
    }

    internal static TimeSpan GetRemainingBootstrapPhaseTimeout(TimeSpan elapsed, TimeSpan totalTimeout)
    {
        var remaining = totalTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private LoadLibraryRemoteOutcome LoadLibraryRemote(IntPtr hProcess, string dllPath, TimeSpan timeout)
    {
        var kernel32 = _api.GetModuleHandle("kernel32.dll");
        var loadLibraryAddr = _api.GetProcAddress(kernel32, "LoadLibraryW");
        if (loadLibraryAddr == IntPtr.Zero)
            return LoadLibraryRemoteOutcome.Failed;

        var dllPathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
        var invocationResult = _bufferInvoker.Invoke(
            hProcess,
            loadLibraryAddr,
            dllPathBytes,
            timeout,
            requireExitCode: true);
        var remoteModuleHandle = invocationResult.ExitCodeAvailable
            ? new IntPtr(unchecked((int)invocationResult.ExitCode))
            : IntPtr.Zero;

        if (invocationResult.Status == RemoteThreadInvocationStatus.TimedOut)
        {
            return LoadLibraryRemoteOutcome.TimedOut;
        }

        return invocationResult.Status == RemoteThreadInvocationStatus.Completed &&
            LoadLibraryRemoteResult.IsSuccessful(
                invocationResult.WaitResult,
                invocationResult.ExitCodeAvailable,
                remoteModuleHandle)
            ? LoadLibraryRemoteOutcome.Succeeded
            : LoadLibraryRemoteOutcome.Failed;
    }

    private string GetErrorMessage(InjectionError error)
    {
        return error switch
        {
            InjectionError.ProcessNotFound => "Process not found or has exited",
            InjectionError.AccessDenied => "Access denied to process",
            InjectionError.ArchitectureMismatch => "Architecture mismatch",
            InjectionError.NotWpfApplication => "Not a WPF application",
            InjectionError.AllocationFailed => "Failed to allocate memory or DLL not found",
            InjectionError.WriteFailed => "Failed to write to process memory",
            InjectionError.CreateThreadFailed => "Failed to create remote thread",
            InjectionError.Timeout => "Injection operation timed out",
            _ => "Unknown error"
        };
    }

    private static InjectionResult CreateSanitizedFailure(
        int processId,
        InjectionError error,
        string publicMessage,
        string diagnosticMessage)
    {
        Trace.WriteLine($"DllInjector sensitive failure for process {processId}: {diagnosticMessage}");
        return InjectionResult.CreateFailure(processId, error, publicMessage);
    }

    private static InjectionResult CreateSanitizedFailure(
        int processId,
        InjectionError error,
        string publicMessage,
        Exception exception)
    {
        Trace.WriteLine($"DllInjector sensitive failure for process {processId}: {exception}");
        return InjectionResult.CreateFailure(processId, error, publicMessage);
    }

}

