using System.Runtime.InteropServices;

namespace WpfDevTools.Injector.Injection;

internal sealed class Win32RemoteInjectionApi : IRemoteInjectionApi
{
    internal static Win32RemoteInjectionApi Instance { get; } = new();

    private Win32RemoteInjectionApi()
    {
    }

    public IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId)
        => OpenProcessNative(desiredAccess, inheritHandle, processId);

    public IntPtr GetModuleHandle(string moduleName)
        => GetModuleHandleNative(moduleName);

    public IntPtr GetProcAddress(IntPtr moduleHandle, string procName)
        => GetProcAddressNative(moduleHandle, procName);

    public IntPtr VirtualAllocEx(
        IntPtr processHandle,
        IntPtr address,
        uint size,
        uint allocationType,
        uint protect)
        => VirtualAllocExNative(processHandle, address, size, allocationType, protect);

    public bool WriteProcessMemory(
        IntPtr processHandle,
        IntPtr baseAddress,
        byte[] buffer,
        uint size,
        out int bytesWritten)
        => WriteProcessMemoryNative(processHandle, baseAddress, buffer, size, out bytesWritten);

    public IntPtr CreateRemoteThread(
        IntPtr processHandle,
        IntPtr threadAttributes,
        uint stackSize,
        IntPtr startAddress,
        IntPtr parameter,
        uint creationFlags,
        IntPtr threadId)
        => CreateRemoteThreadNative(
            processHandle,
            threadAttributes,
            stackSize,
            startAddress,
            parameter,
            creationFlags,
            threadId);

    public uint WaitForSingleObject(IntPtr handle, uint milliseconds)
        => WaitForSingleObjectNative(handle, milliseconds);

    public bool CloseHandle(IntPtr handle)
        => CloseHandleNative(handle);

    public bool VirtualFreeEx(IntPtr processHandle, IntPtr address, int size, uint freeType)
        => VirtualFreeExNative(processHandle, address, size, freeType);

    public bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode)
        => GetExitCodeThreadNative(threadHandle, out exitCode);

    public int GetLastError() => Marshal.GetLastWin32Error();

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "OpenProcess")]
    private static extern IntPtr OpenProcessNative(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, EntryPoint = "GetModuleHandle")]
    private static extern IntPtr GetModuleHandleNative(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "GetProcAddress")]
    private static extern IntPtr GetProcAddressNative(IntPtr moduleHandle, string procName);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "VirtualAllocEx")]
    private static extern IntPtr VirtualAllocExNative(
        IntPtr processHandle,
        IntPtr address,
        uint size,
        uint allocationType,
        uint protect);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WriteProcessMemory")]
    private static extern bool WriteProcessMemoryNative(
        IntPtr processHandle,
        IntPtr baseAddress,
        byte[] buffer,
        uint size,
        out int bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CreateRemoteThread")]
    private static extern IntPtr CreateRemoteThreadNative(
        IntPtr processHandle,
        IntPtr threadAttributes,
        uint stackSize,
        IntPtr startAddress,
        IntPtr parameter,
        uint creationFlags,
        IntPtr threadId);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WaitForSingleObject")]
    private static extern uint WaitForSingleObjectNative(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CloseHandle")]
    private static extern bool CloseHandleNative(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "VirtualFreeEx")]
    private static extern bool VirtualFreeExNative(
        IntPtr processHandle,
        IntPtr address,
        int size,
        uint freeType);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetExitCodeThread")]
    private static extern bool GetExitCodeThreadNative(IntPtr threadHandle, out uint exitCode);
}
