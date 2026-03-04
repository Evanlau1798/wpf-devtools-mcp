using System.Text.Json;
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
    public Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
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
            name = p.ProcessName,
            title = p.WindowTitle,
            architecture = p.Architecture.ToString(),
            dotnetVersion = p.DotNetVersion
        }).ToList();

        return Task.FromResult<object>(new { processes });
    }
}
