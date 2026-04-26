using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using System.Runtime.ExceptionServices;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Utilities;

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
    private readonly int _processId;

    /// <summary>
    /// Create a new RequestDispatcher instance and initialize all handlers
    /// </summary>
    /// <param name="logger">Logger for error reporting</param>
    public RequestDispatcher(FileLogger logger)
        : this(logger, Process.GetCurrentProcess().Id, null)
    {
    }

    internal RequestDispatcher(
        FileLogger logger,
        Func<Dispatcher?, Action, Exception?>? eventTraceCleanupInvoker)
        : this(logger, Process.GetCurrentProcess().Id, eventTraceCleanupInvoker)
    {
    }

    internal RequestDispatcher(
        FileLogger logger,
        int processId,
        Func<Dispatcher?, Action, Exception?>? eventTraceCleanupInvoker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processId = processId;

        var composition = RequestDispatcherRegistry.Create(_logger, eventTraceCleanupInvoker);
        _elementFinder = composition.ElementFinder;
        _eventAnalyzer = composition.EventAnalyzer;
        _handlerMap = composition.HandlerMap.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);

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
        using var requestScope = DispatcherRequestContext.Push(cancellationToken);
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
                    Code = ErrorCode.Timeout,
                    Message = "Request cancelled or timed out",
                    Data = CreateTimeoutRecoveryData()
                }
            };
        }
        catch (TimeoutException)
        {
            return new InspectorResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.Timeout,
                    Message = "Request cancelled or timed out",
                    Data = CreateTimeoutRecoveryData()
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
            processId = _processId,
            protocolVersion = InspectorCompatibilityContract.ProtocolVersion,
            buildFingerprint = InspectorCompatibilityContract.GetBuildFingerprint(typeof(RequestDispatcher))
        };
    }

    internal void AddSimpleHandlerForTesting(
        string method,
        Func<JsonElement?, CancellationToken, Task<object>> handler)
    {
        _simpleHandlers[method] = handler;
    }

    private JsonElement CreateTimeoutRecoveryData()
    {
        var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(InspectorConfig.RequestTimeout.TotalSeconds));
        return JsonSerializer.SerializeToElement(new
        {
            stateAfterTimeoutUnknown = true,
            requiresReconnect = true,
            processId = _processId,
            timeoutSeconds,
            suggestedAction = $"Reconnect to process {_processId} and re-read target state before retrying.",
            hint = "The request was canceled or timed out while dispatcher work may still be pending or running."
        });
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
