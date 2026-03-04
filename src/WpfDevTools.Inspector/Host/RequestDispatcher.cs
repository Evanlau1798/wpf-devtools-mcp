using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Inspector.Host.Handlers;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Dispatches incoming requests to appropriate handlers
/// </summary>
public class RequestDispatcher
{
    private readonly Dictionary<string, IRequestHandler> _handlerMap;
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>> _simpleHandlers;

    public RequestDispatcher()
    {
        // Initialize shared utilities
        var elementFinder = new ElementFinder();
        var xamlSerializer = new XamlSerializer();

        // Initialize analyzers
        var visualTreeAnalyzer = new VisualTreeAnalyzer(elementFinder);
        var bindingAnalyzer = new BindingAnalyzer(elementFinder);
        var logicalTreeAnalyzer = new LogicalTreeAnalyzer(elementFinder);
        var mvvmAnalyzer = new MvvmAnalyzer(elementFinder);
        var dependencyPropertyAnalyzer = new DependencyPropertyAnalyzer(elementFinder);
        var layoutAnalyzer = new LayoutAnalyzer(elementFinder);
        var interactionAnalyzer = new InteractionAnalyzer(elementFinder);
        var styleAnalyzer = new StyleAnalyzer(elementFinder);
        var eventAnalyzer = new EventAnalyzer(elementFinder);
        var performanceAnalyzer = new PerformanceAnalyzer(elementFinder);

        // Initialize handlers
        var handlers = new IRequestHandler[]
        {
            new TreeHandlers(visualTreeAnalyzer, logicalTreeAnalyzer, xamlSerializer, elementFinder),
            new BindingHandlers(bindingAnalyzer, elementFinder),
            new MvvmHandlers(mvvmAnalyzer),
            new DependencyPropertyHandlers(dependencyPropertyAnalyzer),
            new LayoutHandlers(layoutAnalyzer),
            new InteractionHandlers(interactionAnalyzer),
            new StyleHandlers(styleAnalyzer),
            new EventHandlers(eventAnalyzer),
            new PerformanceHandlers(performanceAnalyzer)
        };

        // Build handler map
        _handlerMap = new Dictionary<string, IRequestHandler>();
        foreach (var handler in handlers)
        {
            foreach (var method in handler.GetSupportedMethods())
            {
                _handlerMap[method] = handler;
            }
        }

        // Simple handlers
        _simpleHandlers = new Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>>
        {
            ["ping"] = HandlePingAsync
        };
    }

    /// <summary>
    /// Dispatch request to appropriate handler
    /// </summary>
    public async Task<InspectorResponse> DispatchAsync(
        InspectorRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            object result;

            // Check simple handlers first
            if (_simpleHandlers.TryGetValue(request.Method, out var simpleHandler))
            {
                result = await simpleHandler(request.Params, cancellationToken).ConfigureAwait(false);
            }
            // Check handler map
            else if (_handlerMap.TryGetValue(request.Method, out var handler))
            {
                result = await handler.HandleAsync(request.Method, request.Params, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return new InspectorResponse
                {
                    Id = request.Id,
                    Result = null,
                    Error = new InspectorError
                    {
                        Code = ErrorCode.MethodNotFound,
                        Message = $"Method not found: {request.Method}",
                        Data = null
                    }
                };
            }

            return new InspectorResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToElement(result),
                Error = null
            };
        }
        catch (OperationCanceledException)
        {
            return new InspectorResponse
            {
                Id = request.Id,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.InternalError,
                    Message = "Request cancelled or timed out",
                    Data = null
                }
            };
        }
        catch (ArgumentException ex)
        {
            return new InspectorResponse
            {
                Id = request.Id,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.InvalidParams,
                    Message = ex.Message,
                    Data = null
                }
            };
        }
        catch (Exception ex)
        {
            LogError($"Unhandled exception in DispatchAsync for method '{request.Method}': {ex}");
            return new InspectorResponse
            {
                Id = request.Id,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.InternalError,
                    Message = "Internal inspector error occurred",
                    Data = null
                }
            };
        }
    }

    private async Task<object> HandlePingAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return new { success = true, status = "pong", timestamp = DateTime.UtcNow };
    }

    private static readonly string _logPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"WpfDevTools_Inspector_{System.Diagnostics.Process.GetCurrentProcess().Id}.log");

    private static void LogError(string message)
    {
        try
        {
            System.IO.File.AppendAllText(_logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging errors
        }
    }

}
