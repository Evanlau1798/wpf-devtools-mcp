using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to list all WPF processes
/// </summary>
public class GetProcessesTool
{
    private readonly WpfProcessDetector _detector;

    public GetProcessesTool()
    {
        _detector = new WpfProcessDetector();
    }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        // Parse parameters
        string? nameFilter = null;
        if (parameters != null)
        {
            var paramsType = parameters.GetType();
            var nameFilterProp = paramsType.GetProperty("nameFilter");
            nameFilter = nameFilterProp?.GetValue(parameters)?.ToString();
        }

        // Get all WPF processes
        var allProcesses = _detector.GetAllWpfProcesses();

        // Apply filter if specified
        var filteredProcesses = string.IsNullOrEmpty(nameFilter)
            ? allProcesses
            : allProcesses.Where(p => p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Map to result format
        var processes = filteredProcesses.Select(p => new
        {
            processId = p.ProcessId,
            name = p.ProcessName,
            title = p.WindowTitle,
            architecture = p.Architecture.ToString(),
            dotnetVersion = p.DotNetVersion
        }).ToList();

        return new { processes };
    }
}
