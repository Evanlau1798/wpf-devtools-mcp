using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Base class for MCP tools that communicate with Inspector via Named Pipes
/// </summary>
public abstract class PipeConnectedToolBase
{
    protected readonly SessionManager _sessionManager;

    protected PipeConnectedToolBase(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <summary>
    /// Parse processId and optional elementId from JSON arguments.
    /// Returns (processId, elementId, errorResult). If errorResult is non-null, return it immediately.
    /// </summary>
    public static (int processId, string? elementId, object? error) ParseCommonParams(JsonElement? arguments)
    {
        int? processId = null;
        string? elementId = null;

        if (arguments.HasValue)
        {
            if (arguments.Value.TryGetProperty("processId", out var pidProp))
                processId = pidProp.GetInt32();
            if (arguments.Value.TryGetProperty("elementId", out var eidProp))
                elementId = eidProp.GetString();
        }

        if (!processId.HasValue)
            return (-1, elementId, CreateMissingParamError("processId"));

        return (processId.Value, elementId, null);
    }

    /// <summary>
    /// Parse a string parameter from JSON arguments
    /// </summary>
    protected static string? ParseStringParam(JsonElement? arguments, string paramName)
        => ParameterParser.ParseStringParam(arguments, paramName);

    /// <summary>
    /// Parse an integer parameter from JSON arguments
    /// </summary>
    protected static int? ParseIntParam(JsonElement? arguments, string paramName)
        => ParameterParser.ParseIntParam(arguments, paramName);

    /// <summary>
    /// Create error response for missing required parameter
    /// </summary>
    protected static object CreateMissingParamError(string paramName) =>
        new { success = false, error = $"Missing required parameter: {paramName}" };

    /// <summary>
    /// Create error response for not-connected process
    /// </summary>
    protected static object CreateNotConnectedError(int processId) =>
        new { success = false, error = $"Process {processId} is not connected" };

    /// <summary>
    /// Send a request to the Inspector DLL via Named Pipe
    /// </summary>
    protected async Task<object> SendInspectorRequestAsync(
        int processId, string method, object? parameters, CancellationToken ct)
    {
        if (!_sessionManager.HasSession(processId))
            return CreateNotConnectedError(processId);

        var client = _sessionManager.GetPipeClient(processId);
        if (client == null || !client.IsConnected)
        {
            return new { success = false, error = $"Named pipe not connected for process {processId}" };
        }

        var response = await client.SendRequestAsync(
            Guid.NewGuid().ToString("N"),
            new { method, @params = parameters },
            ct);

        if (response.Error != null)
        {
            return new { success = false, error = response.Error.Message };
        }

        return response.Result.HasValue
            ? (object)response.Result.Value
            : new { success = true };
    }
}
