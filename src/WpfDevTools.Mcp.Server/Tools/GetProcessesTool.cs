using System.Text.Json;
using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to list all WPF processes
/// </summary>
public class GetProcessesTool
{
    private readonly WpfProcessDetector _detector;

    /// <summary>
    /// Initializes a new instance of the GetProcessesTool class
    /// </summary>
    public GetProcessesTool()
    {
        _detector = new WpfProcessDetector();
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
            if (arguments.HasValue && arguments.Value.TryGetProperty("nameFilter", out var filterProp))
                nameFilter = filterProp.GetString();

            var allProcesses = _detector.GetAllWpfProcesses();

            var filteredProcesses = string.IsNullOrEmpty(nameFilter)
                ? allProcesses
                : allProcesses.Where(p => p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var processes = filteredProcesses.Select(p => new
            {
                processId = p.ProcessId,
                processName = p.ProcessName,
                windowTitle = p.WindowTitle,
                architecture = p.Architecture.ToString(),
                dotNetVersion = p.DotNetVersion
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
            return Task.FromResult<object>(new
            {
                success = false,
                error = $"Failed to enumerate processes: {ex.Message}"
            });
        }
    }
}
