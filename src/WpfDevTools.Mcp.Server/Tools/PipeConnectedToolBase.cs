using System.Text.Json;
using WpfDevTools.Shared.Utilities;

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
            {
                if (!pidProp.TryGetInt32(out var parsedPid))
                    return (-1, null, new { success = false, error = "processId must be a valid 32-bit integer" });
                processId = parsedPid;
            }
            if (arguments.Value.TryGetProperty("elementId", out var eidProp))
                elementId = eidProp.GetString();
        }

        if (!processId.HasValue)
            return (-1, elementId, CreateMissingParamError("processId"));

        if (processId.Value <= 0)
            return (-1, elementId, CreateMissingParamError("processId must be a positive integer"));

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
        new { success = false, error = $"Process {processId} is not connected. Call connect(processId: {processId}) first, then retry this tool." };

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
            ct).ConfigureAwait(false);

        if (response.Error != null)
        {
            return new { success = false, error = response.Error.Message };
        }

        return response.Result.HasValue
            ? (object)response.Result.Value
            : new { success = true };
    }
}
