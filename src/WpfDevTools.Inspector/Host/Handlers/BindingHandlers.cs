using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Binding diagnostics related requests
/// </summary>
public class BindingHandlers : IRequestHandler
{
    private readonly BindingAnalyzer _bindingAnalyzer;
    private readonly ElementFinder _elementFinder;

    public BindingHandlers(BindingAnalyzer bindingAnalyzer, ElementFinder elementFinder)
    {
        _bindingAnalyzer = bindingAnalyzer;
        _elementFinder = elementFinder;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_bindings",
            "get_binding_errors",
            "get_datacontext_chain",
            "get_binding_value_chain",
            "force_binding_update"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_bindings" => await HandleGetBindingsAsync(@params, cancellationToken),
            "get_binding_errors" => await HandleGetBindingErrorsAsync(@params, cancellationToken),
            "get_datacontext_chain" => await HandleGetDataContextChainAsync(@params, cancellationToken),
            "get_binding_value_chain" => await HandleGetBindingValueChainAsync(@params, cancellationToken),
            "force_binding_update" => await HandleForceBindingUpdateAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetBindingsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _bindingAnalyzer.GetBindings(elementId), cancellationToken);
    }

    private async Task<object> HandleGetBindingErrorsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var clearAfterRead = ParameterHelpers.GetBoolParam(@params, "clearAfterRead") ?? false;

        return await Task.Run(() =>
            _bindingAnalyzer.GetBindingErrors(clearAfterRead), cancellationToken);
    }

    private async Task<object> HandleGetDataContextChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        if (string.IsNullOrEmpty(elementId))
            throw new ArgumentException("Missing required parameter: elementId");

        return await Task.Run(() =>
            _bindingAnalyzer.GetDataContextChain(elementId), cancellationToken);
    }

    private async Task<object> HandleGetBindingValueChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(elementId))
            throw new ArgumentException("Missing required parameter: elementId");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
        {
            var element = _elementFinder.FindById(elementId!);
            if (element == null)
            {
                return (object)new { success = false, error = "Element not found" };
            }

            return _bindingAnalyzer.GetBindingValueChain(element, propertyName!);
        }, cancellationToken);
    }

    private async Task<object> HandleForceBindingUpdateAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var direction = ParameterHelpers.GetStringParam(@params, "direction") ?? InspectorConstants.BindingDirections.Source;

        if (string.IsNullOrEmpty(elementId))
            throw new ArgumentException("Missing required parameter: elementId");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
        {
            var element = _elementFinder.FindById(elementId!);
            if (element == null)
            {
                return (object)new { success = false, error = "Element not found" };
            }

            return _bindingAnalyzer.ForceBindingUpdate(element, propertyName!, direction);
        }, cancellationToken);
    }
}
