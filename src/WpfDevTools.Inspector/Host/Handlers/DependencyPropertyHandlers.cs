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
            "capture_dp_expression_restore",
            "restore_dp_expression",
            "watch_dp_changes",
            "wait_for_dp_change"
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
            "capture_dp_expression_restore" => await HandleCaptureDpExpressionRestoreAsync(@params, cancellationToken).ConfigureAwait(false),
            "restore_dp_expression" => await HandleRestoreDpExpressionAsync(@params, cancellationToken).ConfigureAwait(false),
            "watch_dp_changes" => await HandleWatchDpChangesAsync(@params, cancellationToken).ConfigureAwait(false),
            "wait_for_dp_change" => await HandleWaitForDpChangeAsync(@params, cancellationToken).ConfigureAwait(false),
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

    private async Task<object> HandleCaptureDpExpressionRestoreAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.CaptureExpressionRestore(propertyName!, elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleRestoreDpExpressionAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var restoreToken = ParameterHelpers.GetStringParam(@params, "restoreToken");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        if (string.IsNullOrEmpty(restoreToken))
            throw new ArgumentException("Missing required parameter: restoreToken");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.RestoreExpression(propertyName!, restoreToken!, elementId), cancellationToken).ConfigureAwait(false);
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

    private async Task<object> HandleWaitForDpChangeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var timeoutMs = ParameterHelpers.GetIntParam(@params, "timeoutMs");
        var pollIntervalMs = ParameterHelpers.GetIntParam(@params, "pollIntervalMs");
        JsonElement? expectedValue = null;
        if (@params.HasValue && @params.Value.TryGetProperty("expectedValue", out var expectedValueProperty))
        {
            expectedValue = expectedValueProperty.Clone();
        }

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            _dependencyPropertyAnalyzer.WaitForChange(propertyName!, elementId, timeoutMs, pollIntervalMs, expectedValue, cancellationToken), cancellationToken).ConfigureAwait(false);
    }
}
