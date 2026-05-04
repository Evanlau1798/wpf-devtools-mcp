using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Host;

public sealed partial class InspectorHost
{
    private async Task HandleClientAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string requestJson;
                try
                {
                    requestJson = await ReadMessageWithSessionTimeoutAsync(stream, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (TimeoutException ex)
                {
                    LogError(ex.Message);
                    break;
                }

                // Parse request with shared options and validate the IPC contract shape.
                var parseResult = DeserializeRequest(requestJson);
                var request = parseResult.Request;
                if (request == null)
                {
                    await SendErrorResponseAsync(
                        stream,
                        parseResult.RequestId,
                        parseResult.ErrorMessage,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var requestLoggingEnabled = _logger.IsEnabled(FileLogLevel.Info);
                Stopwatch? requestLogStopwatch = requestLoggingEnabled
                    ? Stopwatch.StartNew()
                    : null;

                // Process request
                var response = await ProcessRequestAsync(request, cancellationToken).ConfigureAwait(false);

                if (requestLogStopwatch != null)
                {
                    requestLogStopwatch.Stop();
                    _logger.LogRequest(
                        request.Method,
                        request.CorrelationId,
                        _processId,
                        requestLogStopwatch.ElapsedMilliseconds,
                        response.Error == null,
                        response.Error?.Message);
                }

                // Send response
                var responseJson = JsonSerializer.Serialize(response, IpcSerializerOptions);
                await MessageFraming.WriteMessageAsync(stream, responseJson, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Client disconnected
        }
        catch (ObjectDisposedException)
        {
            // Pipe was disposed during shutdown - normal cleanup path
        }
        catch (Exception ex)
        {
            LogError($"Client handling error: {ex.Message}");
        }
    }

    private async Task<string> ReadMessageWithSessionTimeoutAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var readTask = MessageFraming.ReadMessageAsync(stream, cancellationToken);
        var timeoutTask = Task.Delay(_sessionReadTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);

        if (ReferenceEquals(completedTask, readTask))
        {
            return await readTask.ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        ObserveTimedOutReadTask(readTask);

        try
        {
            stream.Dispose();
        }
        catch (Exception ex)
        {
            LogError($"Failed to dispose timed-out client stream: {ex.Message}");
        }

        throw new TimeoutException($"Client session read timed out after {_sessionReadTimeout.TotalMilliseconds}ms");
    }

    private static void ObserveTimedOutReadTask(Task<string> readTask)
    {
        _ = readTask.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<InspectorResponse> ProcessRequestAsync(
        InspectorRequest request,
        CancellationToken cancellationToken)
    {
        // Delegate to RequestDispatcher with timeout
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(InspectorConfig.RequestTimeout);

        try
        {
            return await _dispatcher.DispatchAsync(request, timeoutCts.Token).ConfigureAwait(false);
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }

    private async Task SendErrorResponseAsync(
        Stream stream,
        string requestId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = new InspectorResponse
            {
                Id = requestId,
                Result = null,
                Error = new InspectorError
                {
                    Code = Shared.Enums.ErrorCode.InvalidRequest,
                    Message = errorMessage,
                    Data = null
                }
            };

            var responseJson = JsonSerializer.Serialize(response);
            await MessageFraming.WriteMessageAsync(stream, responseJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send error response: {ex.Message}");
        }
    }

    private void LogError(string message)
    {
        _logger.LogError(message);
    }
}
