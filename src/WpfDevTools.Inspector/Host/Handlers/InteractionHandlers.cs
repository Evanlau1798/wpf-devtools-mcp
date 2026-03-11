using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Interaction related requests
/// </summary>
public class InteractionHandlers : IRequestHandler
{
    private readonly InteractionAnalyzer _interactionAnalyzer;

    /// <summary>
    /// Create a new InteractionHandlers instance
    /// </summary>
    /// <param name="interactionAnalyzer">Interaction analyzer for UI operations</param>
    public InteractionHandlers(InteractionAnalyzer interactionAnalyzer)
    {
        _interactionAnalyzer = interactionAnalyzer;
    }

    /// <summary>
    /// Get list of supported method names
    /// </summary>
    /// <returns>Enumerable of method names this handler supports</returns>
    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "click_element",
            "get_focus_state",
            "focus_element",
            "scroll_to_element",
            "element_screenshot",
            "drag_and_drop",
            "simulate_keyboard"
        };
    }

    /// <summary>
    /// Handle an Inspector request
    /// </summary>
    /// <param name="method">Method name to execute</param>
    /// <param name="params">JSON parameters for the method</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result object from method execution</returns>
    /// <exception cref="InvalidOperationException">Thrown when method is not supported</exception>
    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "click_element" => await HandleClickElementAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_focus_state" => await HandleGetFocusStateAsync(@params, cancellationToken).ConfigureAwait(false),
            "focus_element" => await HandleFocusElementAsync(@params, cancellationToken).ConfigureAwait(false),
            "scroll_to_element" => await HandleScrollToElementAsync(@params, cancellationToken).ConfigureAwait(false),
            "element_screenshot" => await HandleElementScreenshotAsync(@params, cancellationToken).ConfigureAwait(false),
            "drag_and_drop" => await HandleDragAndDropAsync(@params, cancellationToken).ConfigureAwait(false),
            "simulate_keyboard" => await HandleSimulateKeyboardAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleClickElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _interactionAnalyzer.ClickElement(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetFocusStateAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        await Task.CompletedTask;
        return _interactionAnalyzer.GetFocusState(elementId);
    }

    private async Task<object> HandleFocusElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        if (string.IsNullOrEmpty(elementId))
            throw new ArgumentException("Missing required parameter: elementId");

        await Task.CompletedTask;
        return _interactionAnalyzer.FocusElement(elementId);
    }

    private async Task<object> HandleScrollToElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _interactionAnalyzer.ScrollToElement(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleElementScreenshotAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var outputMode = ParameterHelpers.GetStringParam(@params, "outputMode");
        var maxWidth = ParameterHelpers.GetIntParam(@params, "maxWidth");
        var maxHeight = ParameterHelpers.GetIntParam(@params, "maxHeight");

        return await Task.Run(() =>
            _interactionAnalyzer.TakeScreenshot(elementId, outputMode, maxWidth, maxHeight), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleDragAndDropAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var sourceElementId = ParameterHelpers.GetStringParam(@params, "sourceElementId");
        var targetElementId = ParameterHelpers.GetStringParam(@params, "targetElementId");
        var dataFormat = ParameterHelpers.GetStringParam(@params, "dataFormat") ?? InspectorConstants.DataFormats.Text;

        return await Task.Run(() =>
            _interactionAnalyzer.DragAndDrop(sourceElementId, targetElementId, dataFormat), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleSimulateKeyboardAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var key = ParameterHelpers.GetStringParam(@params, "key");
        var eventType = ParameterHelpers.GetStringParam(@params, "eventType") ?? InspectorConstants.KeyboardEvents.KeyDown;

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Missing required parameter: key");

        return await Task.Run(() =>
            _interactionAnalyzer.SimulateKeyboard(elementId, key!, eventType), cancellationToken).ConfigureAwait(false);
    }
}
