using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Mcp.Server;

public sealed partial class NamedPipeClient
{
    /// <summary>
    /// Send request and wait for response.
    /// Uses a SemaphoreSlim to serialize pipe access across async calls.
    /// </summary>
    /// <param name="method">Inspector method name to call</param>
    /// <param name="requestId">Unique request identifier</param>
    /// <param name="requestParams">Parameters to send with the request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<InspectorResponse> SendRequestAsync(
        string method,
        string requestId,
        object? requestParams,
        CancellationToken cancellationToken)
    {
        var lockAcquired = false;
        try
        {
            await _pipeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockAcquired = true;

            using var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeoutCts.CancelAfter(_requestTimeout);
            try
            {
                return await SendRequestCoreAsync(
                    method,
                    requestId,
                    requestParams,
                    requestTimeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (
                requestTimeoutCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                ResetConnectionState();
                throw CreateRequestTimeoutException(ex);
            }
        }
        finally
        {
            if (lockAcquired)
            {
                ResetConnectionStateIfDisposed();
                _pipeSemaphore.Release();
            }
        }
    }

    private TimeoutException CreateRequestTimeoutException(Exception? innerException = null)
    {
        return new TimeoutException(
            $"Timed out waiting {Math.Ceiling(_requestTimeout.TotalMilliseconds)}ms for an Inspector response.",
            innerException);
    }

    private async Task<InspectorResponse> SendRequestCoreAsync(
        string method,
        string requestId,
        object? requestParams,
        CancellationToken cancellationToken)
    {
        var communicationStarted = false;
        try
        {
            // Re-check disposed state after acquiring semaphore
            Stream commStream;
            lock (_lock)
            {
                if (Volatile.Read(ref _disposeState) != 0)
                {
                    throw new ObjectDisposedException(nameof(NamedPipeClient), "Client has been disposed");
                }

                if (_pipeClient == null || !_pipeClient.IsConnected || _communicationStream == null)
                {
                    throw new InvalidOperationException("Client is not connected");
                }
                commStream = _communicationStream;
            }

            var correlationId = Guid.NewGuid().ToString();

            // Create request
            var request = new InspectorRequest
            {
                Id = requestId,
                Method = method,
                Params = requestParams != null ? JsonSerializer.SerializeToElement(requestParams) : null,
                CorrelationId = correlationId
            };

            // Serialize and send
            communicationStarted = true;
            var requestJson = JsonSerializer.Serialize(request, IpcSerializerOptions);
            await MessageFraming.WriteMessageAsync(commStream, requestJson, cancellationToken).ConfigureAwait(false);

            // Read response
            var responseJson = await MessageFraming.ReadMessageAsync(commStream, cancellationToken).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson, IpcSerializerOptions)
                ?? throw new InvalidOperationException("Invalid response from Inspector");

            if (!string.Equals(response.Id, requestId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected response id '{response.Id}' for request '{requestId}'.");
            }

            if (!string.IsNullOrEmpty(response.CorrelationId) &&
                !string.Equals(response.CorrelationId, correlationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected response correlation id '{response.CorrelationId}' for request '{correlationId}'.");
            }

            return response;
        }
        catch (OperationCanceledException) when (communicationStarted)
        {
            ResetConnectionState();
            throw;
        }
        catch (IOException) when (communicationStarted)
        {
            ResetConnectionState();
            throw;
        }
        catch (JsonException ex) when (communicationStarted)
        {
            ResetConnectionState();
            throw new InvalidOperationException("Invalid response from Inspector", ex);
        }
        catch (InvalidOperationException) when (communicationStarted)
        {
            ResetConnectionState();
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            if (communicationStarted)
            {
                ResetConnectionState();
            }

            throw new InvalidOperationException("Pipe connection was closed during communication", ex);
        }
    }

    /// <summary>
    /// Dispose the Named Pipe client and release resources
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        if (_pipeSemaphore.Wait(DisposeInFlightRequestGraceTimeout))
        {
            try
            {
                ResetConnectionState();
            }
            finally
            {
                _pipeSemaphore.Release();
            }
        }
        else
        {
            // The active connect/request owns the pipe. Let it finish or time out,
            // then close the transport before releasing the semaphore.
        }

        // NOTE: _pipeSemaphore is intentionally NOT disposed here.
        // A concurrent SendRequestAsync may still be holding/awaiting the semaphore.
        // Disposing it while awaited would throw ObjectDisposedException.
        // SemaphoreSlim is lightweight and will be collected by GC.

        if (_ownsAuthManager)
        {
            _authManager?.Dispose();
        }
    }
}
