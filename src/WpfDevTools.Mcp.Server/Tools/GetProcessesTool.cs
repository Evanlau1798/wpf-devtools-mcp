using System.Text.Json;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server;
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
    private readonly Func<WpfProcessInfo, McpTargetAuthorization> _targetPolicy;

    /// <summary>
    /// Initializes a new instance of the GetProcessesTool class
    /// </summary>
    public GetProcessesTool()
        : this(new WpfProcessDetector(), CurrentProcessElevationDetector.IsCurrentProcessElevated)
    {
    }

    internal GetProcessesTool(
        WpfProcessDetector detector,
        Func<bool>? isCurrentProcessElevated = null,
        Func<WpfProcessInfo, McpTargetAuthorization>? targetPolicy = null)
    {
        _detector = detector;
        _isCurrentProcessElevated = isCurrentProcessElevated ?? CurrentProcessElevationDetector.IsCurrentProcessElevated;
        _targetPolicy = targetPolicy ?? McpTargetPolicy.Authorize;
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
            var nameFilterError = TryGetOptionalString(arguments, "nameFilter", out var nameFilter);
            if (nameFilterError != null)
            {
                return Task.FromResult(nameFilterError);
            }

            var windowFilterError = TryGetOptionalString(arguments, "windowFilter", out var windowFilterValue);
            if (windowFilterError != null)
            {
                return Task.FromResult(windowFilterError);
            }

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
            var redactedTargetCount = 0;
            var allowedProcesses = new List<WpfProcessInfo>();
            McpTargetAuthorization? discoveryAbort = null;
            foreach (var process in allProcesses)
            {
                var authorization = _targetPolicy(process);
                if (authorization.IsAllowed)
                {
                    allowedProcesses.Add(process);
                    continue;
                }

                redactedTargetCount++;
                if (authorization.ShouldAbortDiscovery && discoveryAbort is null)
                {
                    discoveryAbort = authorization;
                }
            }

            if (discoveryAbort is { } policyFailure)
            {
                return Task.FromResult<object>(new
                {
                    success = false,
                    error = policyFailure.Error,
                    errorCode = policyFailure.ErrorCode,
                    hint = policyFailure.Hint,
                    redactedTargetCount,
                    policyEnvVar = McpServerConfiguration.AllowedTargetsEnvVar
                });
            }

            var filteredProcesses = string.IsNullOrEmpty(nameFilter)
                ? allowedProcesses
                : allowedProcesses.Where(p => p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

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
                    secondaryWindowTitle = p.SecondaryWindowTitle,
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
                    redactedTargetCount,
                    policyEnvVar = McpServerConfiguration.AllowedTargetsEnvVar,
                    message = redactedTargetCount > 0
                        ? "No allowlisted WPF processes found. Configure the MCP target allowlist before discovery can reveal target metadata."
                        : "No WPF processes found. Make sure a WPF application is running."
                });
            }

            return Task.FromResult<object>(new
            {
                success = true,
                processes,
                redactedTargetCount,
                policyEnvVar = McpServerConfiguration.AllowedTargetsEnvVar
            });
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

    private static object? TryGetOptionalString(
        JsonElement? arguments,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return new
            {
                success = false,
                error = $"{propertyName} must be a string when provided",
                errorCode = "InvalidArgument",
                hint = $"Provide {propertyName} as a JSON string value."
            };
        }

        value = property.GetString();
        return null;
    }
}
