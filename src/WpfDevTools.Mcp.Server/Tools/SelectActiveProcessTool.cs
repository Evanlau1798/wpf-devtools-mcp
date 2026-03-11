using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Sets the active connected process used when tools omit processId.
/// </summary>
public sealed class SelectActiveProcessTool
{
    private readonly SessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectActiveProcessTool"/> class.
    /// </summary>
    public SelectActiveProcessTool(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <summary>
    /// Select an already connected process as the active process.
    /// </summary>
    public Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var processId = ParameterParser.ParseIntParam(arguments, "processId");
        if (!processId.HasValue)
        {
            return Task.FromResult<object>(new ToolErrorPayload
            {
                Error = "Missing required parameter: processId",
                ErrorCode = ToolErrorCode.MissingRequiredParameter.ToString(),
                Hint = "Provide the connected processId you want to make active."
            });
        }

        if (!_sessionManager.HasSession(processId.Value))
        {
            return Task.FromResult<object>(new ToolErrorPayload
            {
                Error = $"Process {processId.Value} is not connected. Call connect(processId: {processId.Value}) first.",
                ErrorCode = ToolErrorCode.NotConnected.ToString(),
                Hint = "Connect the target process before selecting it as the active process."
            });
        }

        _sessionManager.SetActiveProcess(processId.Value);
        return Task.FromResult<object>(new
        {
            success = true,
            processId = processId.Value,
            message = $"Process {processId.Value} is now the active process for omitted processId parameters."
        });
    }
}
