using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles DependencyProperty related requests
/// </summary>
public class DependencyPropertyHandlers : IRequestHandler
{
    private readonly DependencyPropertyAnalyzer _dependencyPropertyAnalyzer;

    /// <summary>
    /// Create a new DependencyPropertyHandlers instance
    /// </summary>
    /// <param name="dependencyPropertyAnalyzer">DependencyProperty analyzer</param>
    public DependencyPropertyHandlers(DependencyPropertyAnalyzer dependencyPropertyAnalyzer)
    {
        _dependencyPropertyAnalyzer = dependencyPropertyAnalyzer;
    }

    /// <summary>
    /// Get list of supported method names
    /// </summary>
    /// <returns>Enumerable of method names this handler supports</returns>
    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_dp_value_source",
            "get_dp_metadata",
            "set_dp_value",
            "clear_dp_value",
            "watch_dp_changes"
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
            "get_dp_value_source" => await HandleGetDpValueSourceAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_dp_metadata" => await HandleGetDpMetadataAsync(@params, cancellationToken).ConfigureAwait(false),
            "set_dp_value" => await HandleSetDpValueAsync(@params, cancellationToken).ConfigureAwait(false),
            "clear_dp_value" => await HandleClearDpValueAsync(@params, cancellationToken).ConfigureAwait(false),
            "watch_dp_changes" => await HandleWatchDpChangesAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetDpValueSourceAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var compact = ParameterHelpers.GetBoolParam(@params, "compact") ?? false;

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.GetValueSource(propertyName!, elementId, compact), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleSetDpValueAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var value = ParameterHelpers.GetObjectParam<object>(@params, "value");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        if (value == null)
            throw new ArgumentException("Missing required parameter: value");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.SetValue(propertyName!, value, elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleClearDpValueAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.ClearValue(propertyName!, elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetDpMetadataAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.GetMetadata(propertyName!, elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleWatchDpChangesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.WatchChanges(propertyName!, elementId), cancellationToken).ConfigureAwait(false);
    }
}
