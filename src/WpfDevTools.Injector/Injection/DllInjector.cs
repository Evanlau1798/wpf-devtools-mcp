using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Injects DLL into target process using CreateRemoteThread
/// Based on Snoop's injection mechanism
/// </summary>
public class DllInjector
{
    private const int PROCESS_CREATE_THREAD = 0x0002;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_VM_OPERATION = 0x0008;
    private const int PROCESS_VM_WRITE = 0x0020;
    private const int PROCESS_VM_READ = 0x0010;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint MEM_RELEASE = 0x8000;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

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
        IntPtr allocatedMemory = IntPtr.Zero;

        try
        {
            // Open target process
            hProcess = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false,
                processId);

            if (hProcess == IntPtr.Zero || hProcess == new IntPtr(-1))
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.AccessDenied,
                    $"Failed to open process. Error: {Marshal.GetLastWin32Error()}");
            }

            // Get LoadLibraryW address
            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryW");

            if (loadLibraryAddr == IntPtr.Zero)
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.Unknown,
                    "Failed to get LoadLibraryW address");
            }

            // Allocate memory in target process
            var dllPathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            var size = (uint)dllPathBytes.Length;

            allocatedMemory = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                size,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            if (allocatedMemory == IntPtr.Zero)
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.AllocationFailed,
                    $"Failed to allocate memory. Error: {Marshal.GetLastWin32Error()}");
            }

            // Write DLL path to target process
            if (!WriteProcessMemory(
                hProcess,
                allocatedMemory,
                dllPathBytes,
                size,
                out _))
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.WriteFailed,
                    $"Failed to write memory. Error: {Marshal.GetLastWin32Error()}");
            }

            // Create remote thread
            var hThread = CreateRemoteThread(
                hProcess,
                IntPtr.Zero,
                0,
                loadLibraryAddr,
                allocatedMemory,
                0,
                IntPtr.Zero);

            if (hThread == IntPtr.Zero)
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.CreateThreadFailed,
                    $"Failed to create remote thread. Error: {Marshal.GetLastWin32Error()}");
            }

            // Wait for thread to complete
            var waitResult = WaitForSingleObject(hThread, (uint)timeout.TotalMilliseconds);

            CloseHandle(hThread);

            const uint WAIT_OBJECT_0 = 0x00000000;
            const uint WAIT_TIMEOUT = 0x00000102;
            const uint WAIT_FAILED = 0xFFFFFFFF;

            if (waitResult == WAIT_TIMEOUT)
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.Timeout,
                    "Injection timed out");
            }

            if (waitResult == WAIT_FAILED)
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.Unknown,
                    $"WaitForSingleObject failed with error: {Marshal.GetLastWin32Error()}");
            }

            if (waitResult != WAIT_OBJECT_0)
            {
                return InjectionResult.CreateFailure(
                    processId,
                    InjectionError.Unknown,
                    $"Unexpected wait result: 0x{waitResult:X8}");
            }

            return InjectionResult.CreateSuccess(processId, dllPath);
        }
        catch (Exception ex)
        {
            return InjectionResult.CreateFailure(
                processId,
                InjectionError.Unknown,
                $"Unexpected error: {ex.Message}");
        }
        finally
        {
            // Cleanup
            if (allocatedMemory != IntPtr.Zero && hProcess != IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, allocatedMemory, 0, MEM_RELEASE);
            }

            if (hProcess != IntPtr.Zero)
            {
                CloseHandle(hProcess);
            }
        }
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

    // P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        int dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        uint dwSize,
        uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        uint nSize,
        out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr hProcess,
        IntPtr lpThreadAttributes,
        uint dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        IntPtr hHandle,
        uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        int dwSize,
        uint dwFreeType);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
}
