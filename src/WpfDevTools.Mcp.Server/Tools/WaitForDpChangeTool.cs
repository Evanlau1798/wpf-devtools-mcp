using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to wait for a DependencyProperty change using bounded polling.
/// </summary>
public sealed class WaitForDpChangeTool : PipeConnectedToolBase
{
    public WaitForDpChangeTool(SessionManager sessionManager) : base(sessionManager) { }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        var propertyName = ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        var timeoutMs = ParseIntParam(arguments, "timeoutMs");
        var pollIntervalMs = ParseIntParam(arguments, "pollIntervalMs");
        JsonElement? expectedValue = null;
        if (arguments.HasValue && arguments.Value.TryGetProperty("expectedValue", out var expectedValueProperty))
        {
            expectedValue = expectedValueProperty.Clone();
        }

        return await SendInspectorRequestAsync(
            processId,
            "wait_for_dp_change",
            new
            {
                elementId,
                propertyName,
                timeoutMs,
                pollIntervalMs,
                expectedValue
            },
            cancellationToken);
    }
}
