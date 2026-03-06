using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Base class for MCP tools that communicate with Inspector via Named Pipes
/// </summary>
public abstract class PipeConnectedToolBase
{
    /// <summary>
    /// Session manager for tracking connected processes
    /// </summary>
    protected readonly SessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the PipeConnectedToolBase class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    /// <exception cref="ArgumentNullException">Thrown when sessionManager is null</exception>
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
        var shouldValidateElementId = false;

        if (arguments.HasValue)
        {
            processId = ParameterParser.ParseIntParam(arguments, "processId");

            if (arguments.Value.TryGetProperty("elementId", out var eidProp))
            {
                if (eidProp.ValueKind == JsonValueKind.Null)
                {
                    elementId = null;
                }
                else if (eidProp.ValueKind == JsonValueKind.String)
                {
                    elementId = eidProp.GetString();
                    shouldValidateElementId = true;
                }
                else
                {
                    return (-1, null, CreateInvalidParamError("elementId must be a string when provided"));
                }
            }
        }

        if (!processId.HasValue)
            return (-1, elementId, CreateMissingParamError("processId"));

        if (processId.Value <= 0)
            return (-1, elementId, CreateInvalidParamError("processId must be a positive integer"));

        if (shouldValidateElementId && !ParameterParser.ValidateElementId(elementId, out var elementIdError))
            return (-1, elementId, CreateInvalidParamError(elementIdError!));

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
    /// Create error response for invalid parameter value.
    /// </summary>
    protected static object CreateInvalidParamError(string message) =>
        new { success = false, error = message };

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
        // Get pipe client atomically - avoids TOCTOU race between HasSession and GetPipeClient
        var client = _sessionManager.GetPipeClient(processId);
        if (client == null)
            return CreateNotConnectedError(processId);

        // SECURITY: Check rate limit to prevent DoS attacks (only for connected sessions)
        if (!_sessionManager.CheckRateLimit(processId))
        {
            var availableTokens = _sessionManager.GetAvailableTokens(processId);
            return new
            {
                success = false,
                error = "Rate limit exceeded. Please slow down your requests.",
                availableTokens,
                retryAfterSeconds = 60,
                retryAfter = "Wait 1 minute for rate limit to reset"
            };
        }

        if (!client.IsConnected)
        {
            return new
            {
                success = false,
                error = $"Named pipe not connected for process {processId}. The Inspector DLL may have crashed or the target process exited. Try reconnecting with connect(processId: {processId}).",
                processId,
                suggestedAction = "reconnect"
            };
        }

        var response = await client.SendRequestAsync(
            method,
            Guid.NewGuid().ToString("N"),
            parameters,
            ct).ConfigureAwait(false);

        _sessionManager.UpdateLastActivity(processId);

        if (response.Error != null)
        {
            return CreateInspectorError(response.Error);
        }

        return response.Result.HasValue
            ? (object)response.Result.Value
            : new { success = true };
    }

    private static object CreateInspectorError(InspectorError error)
    {
        return error.Data.HasValue
            ? new
            {
                success = false,
                error = error.Message,
                errorCode = error.Code.ToString(),
                errorData = error.Data.Value
            }
            : new
            {
                success = false,
                error = error.Message,
                errorCode = error.Code.ToString()
            };
    }
}
