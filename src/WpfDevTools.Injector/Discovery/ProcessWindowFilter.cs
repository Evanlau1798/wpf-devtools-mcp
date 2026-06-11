namespace WpfDevTools.Injector.Discovery;

/// <summary>
/// Window visibility scope used for WPF process discovery.
/// </summary>
public enum ProcessWindowFilter
{
    /// <summary>
    /// Include all top-level WPF windows, including background or hidden ones.
    /// </summary>
    All = 0,

    /// <summary>
    /// Include only visible, non-minimized, non-cloaked WPF windows.
    /// </summary>
    Visible = 1,

    /// <summary>
    /// Include only the process that owns the current foreground top-level window.
    /// </summary>
    Foreground = 2
}
