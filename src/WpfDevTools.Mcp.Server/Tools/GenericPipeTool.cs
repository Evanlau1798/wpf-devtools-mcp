using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Generic pipe-connected tool that delegates to the Inspector via Named Pipes.
/// Used for tools that simply forward requests without custom logic.
/// </summary>
public class GenericPipeTool : PipeConnectedToolBase
{
    private readonly string _method;
    private readonly Func<JsonElement?, (int processId, object? parameters, object? error)> _paramExtractor;

    public GenericPipeTool(
        SessionManager sessionManager,
        string method,
        Func<JsonElement?, (int processId, object? parameters, object? error)>? paramExtractor = null)
        : base(sessionManager)
    {
        _method = method;
        _paramExtractor = paramExtractor ?? DefaultParamExtractor;
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, parameters, error) = _paramExtractor(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, _method, parameters, cancellationToken).ConfigureAwait(false);
    }

    private static (int processId, object? parameters, object? error) DefaultParamExtractor(JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return (-1, null, error);
        return (processId, new { elementId }, null);
    }
}
