using System.Text.Json;
using System.Windows;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles DependencyProperty related requests
/// </summary>
public class DependencyPropertyHandlers : IRequestHandler
{
    private readonly DependencyPropertyAnalyzer _dependencyPropertyAnalyzer;

    public DependencyPropertyHandlers(DependencyPropertyAnalyzer dependencyPropertyAnalyzer)
    {
        _dependencyPropertyAnalyzer = dependencyPropertyAnalyzer;
    }

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

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_dp_value_source" => await HandleGetDpValueSourceAsync(@params, cancellationToken),
            "get_dp_metadata" => await HandleGetDpMetadataAsync(@params, cancellationToken),
            "set_dp_value" => await HandleSetDpValueAsync(@params, cancellationToken),
            "clear_dp_value" => await HandleClearDpValueAsync(@params, cancellationToken),
            "watch_dp_changes" => await HandleWatchDpChangesAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetDpValueSourceAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.GetValueSource(propertyName!, elementId)));
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
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.SetValue(propertyName!, value, elementId)));
    }

    private async Task<object> HandleClearDpValueAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.ClearValue(propertyName!, elementId)));
    }

    private async Task<object> HandleGetDpMetadataAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.GetMetadata(propertyName!, elementId)));
    }

    private async Task<object> HandleWatchDpChangesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.WatchChanges(propertyName!, elementId)));
    }
}
