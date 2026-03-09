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

    /// <summary>
    /// Create a new BindingHandlers instance
    /// </summary>
    /// <param name="bindingAnalyzer">Binding analyzer for diagnostics</param>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public BindingHandlers(BindingAnalyzer bindingAnalyzer, ElementFinder elementFinder)
    {
        _bindingAnalyzer = bindingAnalyzer;
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get list of supported method names
    /// </summary>
    /// <returns>Enumerable of method names this handler supports</returns>
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
            "get_bindings" => await HandleGetBindingsAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_binding_errors" => await HandleGetBindingErrorsAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_datacontext_chain" => await HandleGetDataContextChainAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_binding_value_chain" => await HandleGetBindingValueChainAsync(@params, cancellationToken).ConfigureAwait(false),
            "force_binding_update" => await HandleForceBindingUpdateAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetBindingsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var recursive = ParameterHelpers.GetBoolParam(@params, "recursive") ?? false;

        return await Task.Run(() =>
            _bindingAnalyzer.GetBindings(elementId, recursive), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetBindingErrorsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var clearAfterRead = ParameterHelpers.GetBoolParam(@params, "clearAfterRead") ?? false;

        return await Task.Run(() =>
            _bindingAnalyzer.GetBindingErrors(clearAfterRead), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetDataContextChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        // elementId is optional - defaults to null (root element)
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _bindingAnalyzer.GetDataContextChain(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetBindingValueChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        // elementId is optional - defaults to null (root element)
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _bindingAnalyzer.GetBindingValueChain(elementId, propertyName!), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleForceBindingUpdateAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        // elementId is optional - defaults to null (root element)
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var direction = ParameterHelpers.GetStringParam(@params, "direction") ?? InspectorConstants.BindingDirections.Source;

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _bindingAnalyzer.ForceBindingUpdate(elementId, propertyName!, direction), cancellationToken).ConfigureAwait(false);
    }
}
