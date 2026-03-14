using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to drain shared runtime watch events from the Inspector.
/// </summary>
public sealed class DrainEventsTool : PipeConnectedToolBase
{
    public DrainEventsTool(SessionManager sessionManager) : base(sessionManager)
    {
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var maxEvents = ParseIntParam(arguments, "maxEvents");
        if (maxEvents is <= 0)
        {
            return CreateInvalidParamError("maxEvents must be a positive integer when provided");
        }

        var sinceTimestamp = ParseStringParam(arguments, "sinceTimestamp");
        if (sinceTimestamp is not null && !DateTimeOffset.TryParse(sinceTimestamp, out _))
        {
            return CreateInvalidParamError("sinceTimestamp must be a valid ISO-8601 timestamp when provided");
        }

        return await SendInspectorRequestAsync(
            processId,
            "drain_events",
            new
            {
                maxEvents,
                eventTypes = ParseStringArrayParam(arguments, "eventTypes"),
                elementId,
                sinceTimestamp
            },
            cancellationToken).ConfigureAwait(false);
    }
}
