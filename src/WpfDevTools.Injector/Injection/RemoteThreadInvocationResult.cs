namespace WpfDevTools.Injector.Injection;

internal enum RemoteThreadInvocationStatus
{
    Completed,
    AllocationFailed,
    WriteFailed,
    ThreadCreationFailed,
    TimedOut,
    WaitFailed,
    UnexpectedWaitResult,
    ExitCodeUnavailable,
    DeferredCleanupSchedulingFailed
}

internal readonly record struct RemoteThreadInvocationResult(
    RemoteThreadInvocationStatus Status,
    uint WaitResult = 0x00000000,
    bool ExitCodeAvailable = false,
    uint ExitCode = 0,
    int LastError = 0,
    bool DeferredCleanupScheduled = false)
{
    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitTimeout = 0x00000102;
    internal const uint WaitFailed = 0xFFFFFFFF;
}
