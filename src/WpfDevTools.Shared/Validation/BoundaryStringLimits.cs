namespace WpfDevTools.Shared.Validation;

/// <summary>
/// Central string boundary limits for MCP and Inspector IPC request surfaces.
/// </summary>
public static class BoundaryStringLimits
{
    /// <summary>Maximum accepted JSON-RPC or Inspector IPC request id length.</summary>
    public const int MaxJsonRpcIdLength = 128;

    /// <summary>Maximum accepted Inspector method name length.</summary>
    public const int MaxInspectorMethodLength = 128;

    /// <summary>Maximum accepted request correlation id length.</summary>
    public const int MaxCorrelationIdLength = 128;

    /// <summary>Maximum accepted runtime element id length.</summary>
    public const int MaxElementIdLength = 256;

    /// <summary>Maximum accepted human-readable label or selector string length.</summary>
    public const int MaxLabelLength = 256;

    /// <summary>Maximum accepted generic string argument length.</summary>
    public const int MaxStringArgumentLength = 8192;

    /// <summary>Maximum accepted stringified JSON compatibility payload length.</summary>
    public const int MaxStringifiedJsonArgumentLength = 65536;
}
