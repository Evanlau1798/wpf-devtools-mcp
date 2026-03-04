using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Interaction related requests
/// </summary>
public class InteractionHandlers : IRequestHandler
{
    private readonly InteractionAnalyzer _interactionAnalyzer;

    public InteractionHandlers(InteractionAnalyzer interactionAnalyzer)
    {
        _interactionAnalyzer = interactionAnalyzer;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "click_element",
            "scroll_to_element",
            "element_screenshot",
            "drag_and_drop",
            "simulate_keyboard"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "click_element" => await HandleClickElementAsync(@params, cancellationToken),
            "scroll_to_element" => await HandleScrollToElementAsync(@params, cancellationToken),
            "element_screenshot" => await HandleElementScreenshotAsync(@params, cancellationToken),
            "drag_and_drop" => await HandleDragAndDropAsync(@params, cancellationToken),
            "simulate_keyboard" => await HandleSimulateKeyboardAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleClickElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _interactionAnalyzer.ClickElement(elementId), cancellationToken);
    }

    private async Task<object> HandleScrollToElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _interactionAnalyzer.ScrollToElement(elementId), cancellationToken);
    }

    private async Task<object> HandleElementScreenshotAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _interactionAnalyzer.TakeScreenshot(elementId), cancellationToken);
    }

    private async Task<object> HandleDragAndDropAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var sourceElementId = ParameterHelpers.GetStringParam(@params, "sourceElementId");
        var targetElementId = ParameterHelpers.GetStringParam(@params, "targetElementId");
        var dataFormat = ParameterHelpers.GetStringParam(@params, "dataFormat") ?? InspectorConstants.DataFormats.Text;

        return await Task.Run(() =>
            _interactionAnalyzer.DragAndDrop(sourceElementId, targetElementId, dataFormat), cancellationToken);
    }

    private async Task<object> HandleSimulateKeyboardAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var key = ParameterHelpers.GetStringParam(@params, "key");
        var eventType = ParameterHelpers.GetStringParam(@params, "eventType") ?? InspectorConstants.KeyboardEvents.KeyDown;

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Missing required parameter: key");

        return await Task.Run(() =>
            _interactionAnalyzer.SimulateKeyboard(elementId, key!, eventType), cancellationToken);
    }
}
