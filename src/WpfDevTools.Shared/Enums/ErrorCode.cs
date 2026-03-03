namespace WpfDevTools.Shared.Enums;

/// <summary>
/// Error codes for Inspector operations
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// Invalid request format or parameters
    /// </summary>
    InvalidRequest = 1000,

    /// <summary>
    /// Method not found
    /// </summary>
    MethodNotFound = 1001,

    /// <summary>
    /// Internal error in Inspector
    /// </summary>
    InternalError = 1002,

    /// <summary>
    /// Timeout waiting for UI thread
    /// </summary>
    Timeout = 1003,

    /// <summary>
    /// Element not found in Visual Tree
    /// </summary>
    ElementNotFound = 1004,

    /// <summary>
    /// Invalid element identifier
    /// </summary>
    InvalidElement = 1005
}
