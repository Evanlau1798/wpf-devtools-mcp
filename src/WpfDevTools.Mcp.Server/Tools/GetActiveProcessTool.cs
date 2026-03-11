using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Returns the currently selected active process for process-id omission workflows.
/// </summary>
public sealed class GetActiveProcessTool
{
    private readonly SessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetActiveProcessTool"/> class.
    /// </summary>
    public GetActiveProcessTool(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <summary>
    /// Return the current active-process selection.
    /// </summary>
    public Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        if (_sessionManager.TryGetActiveProcessSelection(out var selection) && selection != null)
        {
            return Task.FromResult<object>(new
            {
                success = true,
                hasActiveProcess = true,
                processId = selection.ProcessId,
                selectedAtUtc = selection.SelectedAtUtc
            });
        }

        return Task.FromResult<object>(new
        {
            success = true,
            hasActiveProcess = false
        });
    }
}
