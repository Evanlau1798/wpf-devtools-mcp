namespace WpfDevTools.Shared.Configuration;

/// <summary>
/// Bounded wait limits shared by MCP and inspector-side dependency-property polling.
/// </summary>
public static class DpChangeWaitLimits
{
    /// <summary>Default dependency-property wait timeout in milliseconds.</summary>
    public const int DefaultTimeoutMs = 5000;

    /// <summary>Minimum dependency-property wait timeout in milliseconds.</summary>
    public const int MinTimeoutMs = 1;

    /// <summary>Maximum dependency-property wait timeout in milliseconds.</summary>
    public const int MaxTimeoutMs = 25000;

    /// <summary>Default polling interval for dependency-property waits in milliseconds.</summary>
    public const int DefaultPollIntervalMs = 200;

    /// <summary>Minimum polling interval for dependency-property waits in milliseconds.</summary>
    public const int MinPollIntervalMs = 50;

    /// <summary>Maximum polling interval for dependency-property waits in milliseconds.</summary>
    public const int MaxPollIntervalMs = 5000;
}
