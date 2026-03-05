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

    /// <summary>
    /// Initializes a new instance of the GenericPipeTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    /// <param name="method">Inspector method name to invoke</param>
    /// <param name="paramExtractor">Optional custom parameter extractor function</param>
    public GenericPipeTool(
        SessionManager sessionManager,
        string method,
        Func<JsonElement?, (int processId, object? parameters, object? error)>? paramExtractor = null)
        : base(sessionManager)
    {
        _method = method;
        _paramExtractor = paramExtractor ?? DefaultParamExtractor;
    }

    /// <summary>
    /// Execute the generic tool by forwarding request to Inspector
    /// </summary>
    /// <param name="arguments">JSON arguments to pass to Inspector</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result from Inspector or error</returns>
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
