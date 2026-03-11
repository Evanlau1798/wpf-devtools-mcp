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
            : $"Element '{elementId}' not found",
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
        object? errorData = null;
        if (availableEvents != null)
        {
            errorData = new { availableEvents = availableEvents.ToArray() };
        }

        return Create(
            ToolErrorCode.EventNotFound,
            $"Event '{eventName}' not found",
            "Use a valid eventName from availableEvents or inspect the target control type first.",
            errorData);
    }

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
