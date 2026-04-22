using System.Diagnostics;
using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Utilities;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Inspector.Host.Handlers;
using System.Windows.Threading;
using System.Runtime.ExceptionServices;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Dispatches incoming requests to appropriate handlers
/// </summary>
public sealed class RequestDispatcher : IDisposable
{
    private readonly Dictionary<string, IRequestHandler> _handlerMap;
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>> _simpleHandlers;
    private readonly FileLogger _logger;
    private readonly ElementFinder _elementFinder;
    private readonly EventAnalyzer _eventAnalyzer;

    /// <summary>
    /// Create a new RequestDispatcher instance and initialize all handlers
    /// </summary>
    /// <param name="logger">Logger for error reporting</param>
    public RequestDispatcher(FileLogger logger)
        : this(logger, null)
    {
    }

    internal RequestDispatcher(
        FileLogger logger,
        Func<Dispatcher?, Action, Exception?>? eventTraceCleanupInvoker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize shared utilities
        _elementFinder = new ElementFinder();
        var elementFinder = _elementFinder;
        var xamlSerializer = new XamlSerializer();
        var watchEventBuffer = new WatchEventBuffer();

        // Initialize analyzers
        var visualTreeAnalyzer = new VisualTreeAnalyzer(elementFinder);
        var bindingAnalyzer = new BindingAnalyzer(elementFinder, watchEventBuffer);
        var logicalTreeAnalyzer = new LogicalTreeAnalyzer(elementFinder);
        var elementSearchAnalyzer = new ElementSearchAnalyzer(elementFinder);
        var mvvmAnalyzer = new MvvmAnalyzer(elementFinder, watchEventBuffer);
        var dependencyPropertyAnalyzer = new DependencyPropertyAnalyzer(elementFinder, watchEventBuffer);
        var layoutAnalyzer = new LayoutAnalyzer(elementFinder);
        var interactionAnalyzer = new InteractionAnalyzer(elementFinder, watchEventBuffer);
        var styleAnalyzer = new StyleAnalyzer(elementFinder);
        _eventAnalyzer = new EventAnalyzer(elementFinder, watchEventBuffer, eventTraceCleanupInvoker);
        var performanceAnalyzer = new PerformanceAnalyzer(elementFinder);
        var uiSummaryAnalyzer = new UiSummaryAnalyzer(elementFinder);
        var formSummaryAnalyzer = new FormSummaryAnalyzer(elementFinder);

        // Initialize handlers
        var treeHandlers = new TreeHandlers(visualTreeAnalyzer, logicalTreeAnalyzer, xamlSerializer, elementFinder);
        var elementSearchHandlers = new ElementSearchHandlers(elementSearchAnalyzer);
        var bindingHandlers = new BindingHandlers(bindingAnalyzer, elementFinder);
        var mvvmHandlers = new MvvmHandlers(mvvmAnalyzer);
        var dependencyPropertyHandlers = new DependencyPropertyHandlers(dependencyPropertyAnalyzer);
        var layoutHandlers = new LayoutHandlers(layoutAnalyzer);
        var interactionHandlers = new InteractionHandlers(interactionAnalyzer);
        var styleHandlers = new StyleHandlers(styleAnalyzer);
        var eventHandlers = new EventHandlers(_eventAnalyzer, dependencyPropertyAnalyzer.ClearTransientWatchers);
        var performanceHandlers = new PerformanceHandlers(performanceAnalyzer);
        var sceneSummaryHandlers = new SceneSummaryHandlers(uiSummaryAnalyzer, formSummaryAnalyzer);
        var elementSnapshotHandlers = new ElementSnapshotHandlers(
            treeHandlers,
            bindingHandlers,
            mvvmHandlers,
            styleHandlers,
            layoutHandlers,
            dependencyPropertyHandlers);

        var handlers = new IRequestHandler[]
        {
            treeHandlers,
            elementSearchHandlers,
            bindingHandlers,
            mvvmHandlers,
            dependencyPropertyHandlers,
            layoutHandlers,
            interactionHandlers,
            styleHandlers,
            eventHandlers,
            performanceHandlers,
            sceneSummaryHandlers,
            elementSnapshotHandlers
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
                    CorrelationId = request.CorrelationId,
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
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(result),
                Error = null
            };
        }
        catch (OperationCanceledException)
        {
            return new InspectorResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
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
                CorrelationId = request.CorrelationId,
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
                CorrelationId = request.CorrelationId,
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
        return new
        {
            success = true,
            status = "pong",
            timestamp = DateTime.UtcNow,
            processId = Process.GetCurrentProcess().Id,
            protocolVersion = InspectorCompatibilityContract.ProtocolVersion,
            buildFingerprint = InspectorCompatibilityContract.GetBuildFingerprint(typeof(RequestDispatcher))
        };
    }

    private void LogError(string message)
    {
        _logger.LogError(message);
    }

    /// <summary>
    /// Dispose the ElementFinder to stop its cleanup timer
    /// </summary>
    public void Dispose()
    {
        Exception? disposeException = null;

        try
        {
            _eventAnalyzer.Dispose();
        }
        catch (Exception ex)
        {
            disposeException = ex;
        }
        finally
        {
            _elementFinder.Dispose();
        }

        if (disposeException != null)
        {
            ExceptionDispatchInfo.Capture(disposeException).Throw();
        }
    }
}
