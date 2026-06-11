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
    /// Invalid parameters for method
    /// </summary>
    InvalidParams = 1002,

    /// <summary>
    /// Internal error in Inspector
    /// </summary>
    InternalError = 1003,

    /// <summary>
    /// Timeout waiting for UI thread
    /// </summary>
    Timeout = 1004,

    /// <summary>
    /// Element not found in Visual Tree
    /// </summary>
    ElementNotFound = 1005,

    /// <summary>
    /// Invalid element identifier
    /// </summary>
    InvalidElement = 1006
}
