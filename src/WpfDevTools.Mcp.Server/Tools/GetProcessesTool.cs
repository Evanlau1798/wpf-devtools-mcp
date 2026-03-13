using System.Text.Json;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to list all WPF processes
/// </summary>
public sealed class GetProcessesTool
{
    private readonly WpfProcessDetector _detector;
    private readonly Func<bool> _isCurrentProcessElevated;

    /// <summary>
    /// Initializes a new instance of the GetProcessesTool class
    /// </summary>
    public GetProcessesTool()
        : this(new WpfProcessDetector(), CurrentProcessElevationDetector.IsCurrentProcessElevated)
    {
    }

    internal GetProcessesTool(
        WpfProcessDetector detector,
        Func<bool>? isCurrentProcessElevated = null)
    {
        _detector = detector;
        _isCurrentProcessElevated = isCurrentProcessElevated ?? CurrentProcessElevationDetector.IsCurrentProcessElevated;
    }

    /// <summary>
    /// Execute the get_processes tool to list all running WPF processes
    /// </summary>
    /// <param name="arguments">JSON arguments containing optional nameFilter</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing list of WPF processes or error</returns>
    public Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        try
        {
            string? nameFilter = null;
            string? windowFilterValue = null;
            if (arguments.HasValue && arguments.Value.TryGetProperty("nameFilter", out var filterProp))
                nameFilter = filterProp.GetString();
            if (arguments.HasValue && arguments.Value.TryGetProperty("windowFilter", out var windowFilterProp))
                windowFilterValue = windowFilterProp.GetString();

            if (!ProcessWindowFilters.TryParse(windowFilterValue, out var windowFilter))
            {
                return Task.FromResult<object>(new
                {
                    success = false,
                    error = "windowFilter must be 'all', 'visible', or 'foreground'",
                    errorCode = "InvalidArgument",
                    hint = "Omit windowFilter for the visible-only default, or use all to include background WPF windows."
                });
            }

            var allProcesses = _detector.GetAllWpfProcesses(windowFilter);
            var currentProcessIsElevated = _isCurrentProcessElevated();

            var filteredProcesses = string.IsNullOrEmpty(nameFilter)
                ? allProcesses
                : allProcesses.Where(p => p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var processes = filteredProcesses.Select(p =>
            {
                var access = ProcessConnectionAccessEvaluator.Evaluate(
                    p.ProcessId,
                    p.IsElevated,
                    currentProcessIsElevated);

                return new
                {
                    processId = p.ProcessId,
                    processName = p.ProcessName,
                    windowTitle = p.WindowTitle,
                    architecture = p.Architecture.ToString(),
                    dotNetVersion = p.DotNetVersion,
                    runtime = p.Runtime.ToString(),
                    isElevated = p.IsElevated,
                    requiresElevationToConnect = access.RequiresElevationToConnect,
                    canConnectFromCurrentServer = access.CanConnectFromCurrentServer,
                    connectionWarning = access.ConnectionWarning
                };
            }).ToList();

            if (processes.Count == 0)
            {
                return Task.FromResult<object>(new
                {
                    success = true,
                    processes = Array.Empty<object>(),
                    message = "No WPF processes found. Make sure a WPF application is running."
                });
            }

            return Task.FromResult<object>(new { success = true, processes });
        }
        catch (Exception ex)
        {
            var (errorCode, message) = ToolCallHelper.ClassifyException(ex);
            return Task.FromResult<object>(new ToolErrorPayload
            {
                Error = message,
                ErrorCode = errorCode,
                Hint = "Retry get_processes, or inspect local process permissions and server logs if enumeration keeps failing."
            });
        }
    }
}
