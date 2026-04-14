namespace WpfDevTools.Injector.Injection;

internal interface IRemoteAllocationCleanupScheduler
{
    bool TryScheduleRelease(
        IntPtr processHandle,
        IntPtr threadHandle,
        IntPtr remoteAddress);
}
