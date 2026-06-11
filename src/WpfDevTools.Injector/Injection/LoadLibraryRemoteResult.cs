namespace WpfDevTools.Injector.Injection;

internal static class LoadLibraryRemoteResult
{
    internal const uint WaitObject0 = 0x00000000;

    public static bool IsSuccessful(
        uint waitResult,
        bool exitCodeAvailable,
        IntPtr remoteModuleHandle)
    {
        return waitResult == WaitObject0 &&
            exitCodeAvailable &&
            remoteModuleHandle != IntPtr.Zero;
    }
}
