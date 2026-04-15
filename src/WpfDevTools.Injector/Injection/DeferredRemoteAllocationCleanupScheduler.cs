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

            var registration = new RemoteAllocationCleanupRegistration(
                duplicatedProcessHandle,
                remoteAddress,
                waitHandle,
                ReleaseRemoteAllocation);
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

    private static void ReleaseRemoteAllocation(IntPtr processHandle, IntPtr remoteAddress)
    {
        try
        {
            VirtualFreeEx(processHandle, remoteAddress, 0, MEM_RELEASE);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    internal sealed class ThreadPoolDeferredCleanupWaitRegistration : IDeferredCleanupWaitRegistration
    {
        private readonly RegisteredWaitHandle _registration;

        public ThreadPoolDeferredCleanupWaitRegistration(RegisteredWaitHandle registration)
        {
            _registration = registration;
        }

        public void Unregister()
        {
            _registration.Unregister(null);
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

internal interface IDeferredCleanupWaitRegistration
{
    void Unregister();
}

internal sealed class RemoteAllocationCleanupRegistration
{
    private readonly object _syncRoot = new();
    private readonly IntPtr _processHandle;
    private readonly IntPtr _remoteAddress;
    private readonly WaitHandle _waitHandle;
    private readonly Action<IntPtr, IntPtr> _releaseRemoteAllocation;
    private IDeferredCleanupWaitRegistration? _registration;
    private bool _released;

    public RemoteAllocationCleanupRegistration(
        IntPtr processHandle,
        IntPtr remoteAddress,
        WaitHandle waitHandle,
        Action<IntPtr, IntPtr> releaseRemoteAllocation)
    {
        _processHandle = processHandle;
        _remoteAddress = remoteAddress;
        _waitHandle = waitHandle;
        _releaseRemoteAllocation = releaseRemoteAllocation;
    }

    public void Register()
    {
        Register(static (waitHandle, callback) =>
        {
            var registeredWait = ThreadPool.RegisterWaitForSingleObject(
                waitHandle,
                static (state, _) => ((Action)state!).Invoke(),
                callback,
                Timeout.Infinite,
                true);

            return new DeferredRemoteAllocationCleanupScheduler.ThreadPoolDeferredCleanupWaitRegistration(registeredWait);
        });
    }

    internal void Register(Func<WaitHandle, Action, IDeferredCleanupWaitRegistration> registerWait)
    {
        var waitRegistration = registerWait(_waitHandle, Release);
        var unregisterImmediately = false;

        lock (_syncRoot)
        {
            if (_released)
            {
                unregisterImmediately = true;
            }
            else
            {
                _registration = waitRegistration;
            }
        }

        if (unregisterImmediately)
        {
            waitRegistration.Unregister();
        }
    }

    private void Release()
    {
        IDeferredCleanupWaitRegistration? registration;
        lock (_syncRoot)
        {
            if (_released)
            {
                return;
            }

            _released = true;
            registration = _registration;
        }

        try
        {
            _releaseRemoteAllocation(_processHandle, _remoteAddress);
        }
        finally
        {
            registration?.Unregister();
            _waitHandle.Dispose();
        }
    }
}
