using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles MVVM related requests
/// </summary>
public class MvvmHandlers : IRequestHandler
{
    private readonly MvvmAnalyzer _mvvmAnalyzer;

    public MvvmHandlers(MvvmAnalyzer mvvmAnalyzer)
    {
        _mvvmAnalyzer = mvvmAnalyzer;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_viewmodel",
            "get_commands",
            "execute_command",
            "modify_viewmodel",
            "get_validation_errors"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_viewmodel" => await HandleGetViewModelAsync(@params, cancellationToken),
            "get_commands" => await HandleGetCommandsAsync(@params, cancellationToken),
            "execute_command" => await HandleExecuteCommandAsync(@params, cancellationToken),
            "modify_viewmodel" => await HandleModifyViewModelAsync(@params, cancellationToken),
            "get_validation_errors" => await HandleGetValidationErrorsAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetViewModelAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _mvvmAnalyzer.GetViewModel(elementId), cancellationToken);
    }

    private async Task<object> HandleGetCommandsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _mvvmAnalyzer.GetCommands(elementId), cancellationToken);
    }

    private async Task<object> HandleExecuteCommandAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var commandName = ParameterHelpers.GetStringParam(@params, "commandName");
        var parameter = ParameterHelpers.GetStringParam(@params, "parameter");

        if (string.IsNullOrEmpty(commandName))
            throw new ArgumentException("Missing required parameter: commandName");

        return await Task.Run(() =>
            _mvvmAnalyzer.ExecuteCommand(elementId, commandName!, parameter), cancellationToken);
    }

    private async Task<object> HandleModifyViewModelAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var value = ParameterHelpers.GetObjectParam<object>(@params, "value");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        if (value == null)
            throw new ArgumentException("Missing required parameter: value");

        return await Task.Run(() =>
            _mvvmAnalyzer.ModifyViewModel(elementId, propertyName!, value), cancellationToken);
    }

    private async Task<object> HandleGetValidationErrorsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _mvvmAnalyzer.GetValidationErrors(elementId), cancellationToken);
    }
}
