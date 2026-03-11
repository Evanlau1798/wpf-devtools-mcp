using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Routes the read-only find_elements inspector method.
/// </summary>
public sealed class ElementSearchHandlers : IRequestHandler
{
    private readonly ElementSearchAnalyzer _elementSearchAnalyzer;

    /// <summary>
    /// Initializes a new handler for element search requests.
    /// </summary>
    public ElementSearchHandlers(ElementSearchAnalyzer elementSearchAnalyzer)
    {
        _elementSearchAnalyzer = elementSearchAnalyzer;
    }

    /// <summary>
    /// Gets the inspector method names supported by this handler.
    /// </summary>
    public IEnumerable<string> GetSupportedMethods()
    {
        return new[] { "find_elements" };
    }

    /// <summary>
    /// Handles element search requests.
    /// </summary>
    public Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<object>(_elementSearchAnalyzer.FindElements(
            rootElementId: ParameterHelpers.GetStringParam(@params, "elementId"),
            typeName: ParameterHelpers.GetStringParam(@params, "typeName"),
            typeNames: ParameterHelpers.GetStringArrayParam(@params, "typeNames"),
            elementName: ParameterHelpers.GetStringParam(@params, "elementName"),
            automationId: ParameterHelpers.GetStringParam(@params, "automationId"),
            propertyName: ParameterHelpers.GetStringParam(@params, "propertyName"),
            propertyValue: ParameterHelpers.GetStringParam(@params, "propertyValue"),
            maxResults: ParameterHelpers.GetIntParam(@params, "maxResults"),
            matchMode: ParameterHelpers.GetStringParam(@params, "matchMode")));
    }
}
