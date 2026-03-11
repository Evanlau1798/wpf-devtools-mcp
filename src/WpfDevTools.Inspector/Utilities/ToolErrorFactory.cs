using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Creates consistent Inspector-side error payloads for AI recovery workflows.
/// </summary>
public static class ToolErrorFactory
{
    /// <summary>
    /// Create an element-not-found error payload.
    /// </summary>
    public static ToolErrorPayload ElementNotFound(string? elementId = null) => Create(
        ToolErrorCode.ElementNotFound,
        elementId is null
            ? "Element not found"
            : $"Element not found: '{elementId}'",
        "Call get_visual_tree or get_logical_tree first to confirm the target elementId.");

    /// <summary>
    /// Create a property-not-found error payload.
    /// </summary>
    public static ToolErrorPayload PropertyNotFound(string propertyName, string ownerType) => Create(
        ToolErrorCode.PropertyNotFound,
        $"Property '{propertyName}' not found on {ownerType}",
        "Verify the propertyName spelling and target element/ViewModel type before retrying.");

    /// <summary>
    /// Create an event-not-found error payload.
    /// </summary>
    public static ToolErrorPayload EventNotFound(string eventName, IEnumerable<string>? availableEvents = null)
    {
        string[]? availableEventsArray = null;
        object? errorData = null;
        if (availableEvents != null)
        {
            availableEventsArray = availableEvents.ToArray();
            errorData = new { availableEvents = availableEventsArray };
        }

        return new ToolErrorPayload
        {
            Error = $"Event '{eventName}' not found",
            ErrorCode = ToolErrorCode.EventNotFound.ToString(),
            Hint = "Use a valid eventName from availableEvents or inspect the target control type first.",
            ErrorData = errorData,
            AvailableEvents = availableEventsArray
        };
    }

    /// <summary>
    /// Create an element-not-clickable error payload.
    /// </summary>
    public static ToolErrorPayload ElementNotClickable(string elementType) => Create(
        ToolErrorCode.ElementNotClickable,
        "Element is not clickable",
        $"Target a ButtonBase descendant or TabItem instead. Current elementType is '{elementType}'.",
        new { elementType });

    /// <summary>
    /// Create a command-not-found error payload.
    /// </summary>
    public static ToolErrorPayload CommandNotFound(string commandName) => Create(
        ToolErrorCode.CommandNotFound,
        $"Command '{commandName}' not found",
        "Call get_commands first to inspect the available ICommand names on the current DataContext.");

    /// <summary>
    /// Create an invalid-argument error payload.
    /// </summary>
    public static ToolErrorPayload InvalidArgument(string message, string? hint = null) => Create(
        ToolErrorCode.InvalidArgument,
        message,
        hint);

    /// <summary>
    /// Create an element-not-loaded error payload.
    /// </summary>
    public static ToolErrorPayload ElementNotLoaded(string message, string? hint = null) => Create(
        ToolErrorCode.ElementNotLoaded,
        message,
        hint ?? "The element may not be loaded yet. Activate its parent window or tab and retry.");

    /// <summary>
    /// Create a structured runtime-failure payload for operations that failed after input validation.
    /// </summary>
    public static ToolErrorPayload OperationFailed(
        string operationDescription,
        Exception exception,
        string? hint = null,
        object? errorData = null) => Create(
        ToolErrorCode.OperationFailed,
        $"Failed to {operationDescription}: {exception.Message}",
        hint ?? "Retry after re-querying the current target state. If the issue persists, refresh the target element or reconnect to the process.",
        errorData);

    /// <summary>
    /// Create a structured error payload from primitive parts.
    /// </summary>
    public static ToolErrorPayload Create(
        ToolErrorCode errorCode,
        string message,
        string? hint = null,
        object? errorData = null) =>
        new()
        {
            Error = message,
            ErrorCode = errorCode.ToString(),
            Hint = hint,
            ErrorData = errorData
        };
}
