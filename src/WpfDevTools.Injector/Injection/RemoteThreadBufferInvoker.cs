namespace WpfDevTools.Injector.Injection;

internal sealed class RemoteThreadBufferInvoker
{
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint MEM_RELEASE = 0x8000;

    private readonly IRemoteInjectionApi _api;
    private readonly IRemoteAllocationCleanupScheduler _cleanupScheduler;

    internal RemoteThreadBufferInvoker(
        IRemoteInjectionApi api,
        IRemoteAllocationCleanupScheduler cleanupScheduler)
    {
        _api = api;
        _cleanupScheduler = cleanupScheduler;
    }

    internal RemoteThreadInvocationResult Invoke(
        IntPtr processHandle,
        IntPtr startAddress,
        byte[] parameterBytes,
        TimeSpan timeout,
        bool requireExitCode)
    {
        var remoteBuffer = _api.VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            (uint)parameterBytes.Length,
            MEM_COMMIT | MEM_RESERVE,
            PAGE_READWRITE);

        if (remoteBuffer == IntPtr.Zero)
        {
            return new RemoteThreadInvocationResult(
                RemoteThreadInvocationStatus.AllocationFailed,
                LastError: _api.GetLastError());
        }

        if (!_api.WriteProcessMemory(
                processHandle,
                remoteBuffer,
                parameterBytes,
                (uint)parameterBytes.Length,
                out _))
        {
            var lastError = _api.GetLastError();
            _api.VirtualFreeEx(processHandle, remoteBuffer, 0, MEM_RELEASE);
            return new RemoteThreadInvocationResult(
                RemoteThreadInvocationStatus.WriteFailed,
                LastError: lastError);
        }

        var threadHandle = _api.CreateRemoteThread(
            processHandle,
            IntPtr.Zero,
            0,
            startAddress,
            remoteBuffer,
            0,
            IntPtr.Zero);

        if (threadHandle == IntPtr.Zero)
        {
            var lastError = _api.GetLastError();
            _api.VirtualFreeEx(processHandle, remoteBuffer, 0, MEM_RELEASE);
            return new RemoteThreadInvocationResult(
                RemoteThreadInvocationStatus.ThreadCreationFailed,
                LastError: lastError);
        }

        try
        {
            var waitResult = _api.WaitForSingleObject(threadHandle, (uint)timeout.TotalMilliseconds);
            if (waitResult == RemoteThreadInvocationResult.WaitObject0)
            {
                uint exitCode = 0;
                var exitCodeAvailable = !requireExitCode || _api.GetExitCodeThread(threadHandle, out exitCode);
                _api.VirtualFreeEx(processHandle, remoteBuffer, 0, MEM_RELEASE);

                return exitCodeAvailable
                    ? new RemoteThreadInvocationResult(
                        RemoteThreadInvocationStatus.Completed,
                        WaitResult: waitResult,
                        ExitCodeAvailable: requireExitCode,
                        ExitCode: requireExitCode ? exitCode : 0)
                    : new RemoteThreadInvocationResult(
                        RemoteThreadInvocationStatus.ExitCodeUnavailable,
                        WaitResult: waitResult);
            }

            var waitError = waitResult == RemoteThreadInvocationResult.WaitFailed
                ? _api.GetLastError()
                : 0;
            var cleanupScheduled = _cleanupScheduler.TryScheduleRelease(
                processHandle,
                threadHandle,
                remoteBuffer);
            if (!cleanupScheduled)
            {
                return new RemoteThreadInvocationResult(
                    RemoteThreadInvocationStatus.DeferredCleanupSchedulingFailed,
                    WaitResult: waitResult,
                    LastError: waitError);
            }

            return waitResult switch
            {
                RemoteThreadInvocationResult.WaitTimeout => new RemoteThreadInvocationResult(
                    RemoteThreadInvocationStatus.TimedOut,
                    WaitResult: waitResult,
                    DeferredCleanupScheduled: cleanupScheduled),
                RemoteThreadInvocationResult.WaitFailed => new RemoteThreadInvocationResult(
                    RemoteThreadInvocationStatus.WaitFailed,
                    WaitResult: waitResult,
                    LastError: waitError,
                    DeferredCleanupScheduled: cleanupScheduled),
                _ => new RemoteThreadInvocationResult(
                    RemoteThreadInvocationStatus.UnexpectedWaitResult,
                    WaitResult: waitResult,
                    LastError: waitError,
                    DeferredCleanupScheduled: cleanupScheduled)
            };
        }
        finally
        {
            _api.CloseHandle(threadHandle);
        }
    }
}
