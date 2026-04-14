using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WpfDevTools.Injector.Injection;

internal sealed class DeferredRemoteAllocationCleanupScheduler : IRemoteAllocationCleanupScheduler
{
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint MEM_RELEASE = 0x8000;

    internal static DeferredRemoteAllocationCleanupScheduler Instance { get; } = new();

    private DeferredRemoteAllocationCleanupScheduler()
    {
    }

    public bool TryScheduleRelease(
        IntPtr processHandle,
        IntPtr threadHandle,
        IntPtr remoteAddress)
    {
        if (processHandle == IntPtr.Zero ||
            threadHandle == IntPtr.Zero ||
            remoteAddress == IntPtr.Zero)
        {
            return false;
        }

        if (!TryDuplicateHandle(processHandle, out var duplicatedProcessHandle))
        {
            return false;
        }

        if (!TryDuplicateHandle(threadHandle, out var duplicatedThreadHandle))
        {
            CloseHandle(duplicatedProcessHandle);
            return false;
        }

        EventWaitHandle? waitHandle = null;
        try
        {
            waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
            waitHandle.SafeWaitHandle = new SafeWaitHandle(duplicatedThreadHandle, ownsHandle: true);
            duplicatedThreadHandle = IntPtr.Zero;

            var registration = new CleanupRegistration(
                duplicatedProcessHandle,
                remoteAddress,
                waitHandle);
            registration.Register();
            return true;
        }
        catch
        {
            waitHandle?.Dispose();
            if (duplicatedThreadHandle != IntPtr.Zero)
            {
                CloseHandle(duplicatedThreadHandle);
            }

            CloseHandle(duplicatedProcessHandle);
            return false;
        }
    }

    private static bool TryDuplicateHandle(IntPtr sourceHandle, out IntPtr duplicatedHandle)
    {
        var currentProcess = GetCurrentProcess();
        return DuplicateHandle(
            currentProcess,
            sourceHandle,
            currentProcess,
            out duplicatedHandle,
            0,
            false,
            DUPLICATE_SAME_ACCESS);
    }

    private sealed class CleanupRegistration
    {
        private readonly IntPtr _processHandle;
        private readonly IntPtr _remoteAddress;
        private readonly WaitHandle _waitHandle;
        private RegisteredWaitHandle? _registration;
        private int _released;

        public CleanupRegistration(
            IntPtr processHandle,
            IntPtr remoteAddress,
            WaitHandle waitHandle)
        {
            _processHandle = processHandle;
            _remoteAddress = remoteAddress;
            _waitHandle = waitHandle;
        }

        public void Register()
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _waitHandle,
                static (state, _) => ((CleanupRegistration)state!).Release(),
                this,
                Timeout.Infinite,
                true);
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            try
            {
                VirtualFreeEx(_processHandle, _remoteAddress, 0, MEM_RELEASE);
            }
            finally
            {
                _registration?.Unregister(null);
                _waitHandle.Dispose();
                CloseHandle(_processHandle);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        out IntPtr targetHandle,
        uint desiredAccess,
        bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(
        IntPtr processHandle,
        IntPtr address,
        int size,
        uint freeType);
}
