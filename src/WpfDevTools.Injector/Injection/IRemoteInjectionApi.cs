namespace WpfDevTools.Injector.Injection;

internal interface IRemoteInjectionApi
{
    IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    IntPtr GetModuleHandle(string moduleName);

    IntPtr GetProcAddress(IntPtr moduleHandle, string procName);

    IntPtr VirtualAllocEx(
        IntPtr processHandle,
        IntPtr address,
        uint size,
        uint allocationType,
        uint protect);

    bool WriteProcessMemory(
        IntPtr processHandle,
        IntPtr baseAddress,
        byte[] buffer,
        uint size,
        out int bytesWritten);

    IntPtr CreateRemoteThread(
        IntPtr processHandle,
        IntPtr threadAttributes,
        uint stackSize,
        IntPtr startAddress,
        IntPtr parameter,
        uint creationFlags,
        IntPtr threadId);

    uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    bool CloseHandle(IntPtr handle);

    bool VirtualFreeEx(IntPtr processHandle, IntPtr address, int size, uint freeType);

    bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode);

    int GetLastError();
}
