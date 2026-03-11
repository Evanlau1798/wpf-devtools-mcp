namespace WpfDevTools.Shared.ErrorHandling;

/// <summary>
/// Stable machine-readable error codes for MCP tool recovery logic.
/// </summary>
public enum ToolErrorCode
{
    /// <summary>
    /// A required parameter was missing.
    /// </summary>
    MissingRequiredParameter,

    /// <summary>
    /// A provided argument value was invalid.
    /// </summary>
    InvalidArgument,

    /// <summary>
    /// The target process is not connected to an inspector session.
    /// </summary>
    NotConnected,

    /// <summary>
    /// No active process has been selected for process-id omission workflows.
    /// </summary>
    NoActiveProcess,

    /// <summary>
    /// The target element could not be resolved.
    /// </summary>
    ElementNotFound,

    /// <summary>
    /// The requested property does not exist on the target.
    /// </summary>
    PropertyNotFound,

    /// <summary>
    /// The requested routed event does not exist on the target.
    /// </summary>
    EventNotFound,

    /// <summary>
    /// The target element exists but is not loaded into a usable presentation source.
    /// </summary>
    ElementNotLoaded
}
