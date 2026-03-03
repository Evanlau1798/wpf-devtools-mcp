using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Dispatches incoming requests to appropriate handlers
/// </summary>
public class RequestDispatcher
{
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>> _handlers;

    public RequestDispatcher()
    {
        _handlers = new Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>>
        {
            ["ping"] = HandlePingAsync,
            ["test_slow"] = HandleTestSlowAsync,
            ["get_visual_tree"] = HandleGetVisualTreeAsync
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
            // Check if method exists
            if (!_handlers.ContainsKey(request.Method))
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

            // Execute handler
            var handler = _handlers[request.Method];
            var result = await handler(request.Params, cancellationToken);

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
            return new InspectorResponse
            {
                Id = request.Id,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.InternalError,
                    Message = ex.Message,
                    Data = null
                }
            };
        }
    }

    private async Task<object> HandlePingAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning
        return new { status = "pong" };
    }

    private async Task<object> HandleTestSlowAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        // Simulate slow operation for testing timeout
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        return new { status = "completed" };
    }

    private async Task<object> HandleGetVisualTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        // Validate parameters
        if (@params == null || !@params.Value.TryGetProperty("elementId", out _))
        {
            throw new ArgumentException("Missing required parameter: elementId");
        }

        return new { tree = "placeholder" };
    }
}
