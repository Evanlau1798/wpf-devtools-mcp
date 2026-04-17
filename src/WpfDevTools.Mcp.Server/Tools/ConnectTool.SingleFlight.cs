using System.Threading;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private Task<object> RunSingleFlightAsync(
        int processId,
        CancellationToken callerCancellationToken,
        Func<CancellationToken, Task<object>> operationFactory)
    {
        ArgumentNullException.ThrowIfNull(operationFactory);

        while (true)
        {
            if (_inflightConnects.TryGetValue(processId, out var existingOperation))
            {
                existingOperation.AddWaiter();
                return existingOperation.WaitAsync(callerCancellationToken);
            }

            var operation = new InflightConnectOperation();
            operation.AddWaiter();
            if (_inflightConnects.TryAdd(processId, operation))
            {
                _ = Task.Run(() => CompleteSingleFlightAsync(processId, operation, operationFactory));
                return operation.WaitAsync(callerCancellationToken);
            }
        }
    }

    private async Task CompleteSingleFlightAsync(
        int processId,
        InflightConnectOperation operation,
        Func<CancellationToken, Task<object>> operationFactory)
    {
        try
        {
            operation.SetResult(await operationFactory(operation.CancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            operation.SetException(ex);
        }
        finally
        {
            _inflightConnects.TryRemove(new KeyValuePair<int, InflightConnectOperation>(processId, operation));
            operation.Dispose();
        }
    }

    private sealed class InflightConnectOperation : IDisposable
    {
        private readonly TaskCompletionSource<object> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cancellationSource = new();
        private int _activeWaiters;

        public CancellationToken CancellationToken => _cancellationSource.Token;

        public void AddWaiter()
        {
            Interlocked.Increment(ref _activeWaiters);
        }

        public Task<object> WaitAsync(CancellationToken callerCancellationToken)
        {
            if (!callerCancellationToken.CanBeCanceled)
            {
                return _completion.Task;
            }

            return WaitWithCallerCancellationAsync(callerCancellationToken);
        }

        public void SetResult(object result)
        {
            _completion.TrySetResult(result);
        }

        public void SetException(Exception exception)
        {
            _completion.TrySetException(exception);
        }

        public void Dispose()
        {
            _cancellationSource.Dispose();
        }

        private async Task<object> WaitWithCallerCancellationAsync(CancellationToken callerCancellationToken)
        {
            try
            {
                return await _completion.Task.WaitAsync(callerCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
            {
                var lastWaiterCancelled = false;
                if (!_completion.Task.IsCompleted && Interlocked.Decrement(ref _activeWaiters) == 0)
                {
                    lastWaiterCancelled = true;
                    try
                    {
                        _cancellationSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                if (lastWaiterCancelled)
                {
                    try
                    {
                        await _completion.Task.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }
    }
}