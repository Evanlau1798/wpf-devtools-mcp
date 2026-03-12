namespace WpfDevTools.Injector.Discovery;

internal sealed record TopLevelWindowSnapshot(
    int ProcessId,
    IntPtr Handle,
    string? Title,
    string? ClassName,
    bool IsVisible,
    bool IsMinimized = false,
    bool IsCloaked = false,
    bool IsForeground = false);
