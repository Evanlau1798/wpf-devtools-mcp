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
        var operationKey = new ConnectOperationKey(_sessionManager, processId);

        while (true)
        {
            if (GlobalInflightConnects.TryGetValue(operationKey, out var existingOperation))
            {
                if (existingOperation.TryAddWaiter())
                {
                    return existingOperation.WaitAsync(callerCancellationToken);
                }

                return WaitForClosingOperationAndRetryAsync(
                    processId,
                    existingOperation,
                    callerCancellationToken,
                    operationFactory);
            }

            var operation = new InflightConnectOperation();
            operation.AddWaiter();
            if (GlobalInflightConnects.TryAdd(operationKey, operation))
            {
                _ = Task.Run(() => CompleteSingleFlightAsync(operationKey, operation, operationFactory));
                return operation.WaitAsync(callerCancellationToken);
            }
        }
    }

    private async Task CompleteSingleFlightAsync(
        ConnectOperationKey operationKey,
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
            GlobalInflightConnects.TryRemove(new KeyValuePair<ConnectOperationKey, InflightConnectOperation>(operationKey, operation));
            operation.Dispose();
        }
    }

    private async Task<object> WaitForClosingOperationAndRetryAsync(
        int processId,
        InflightConnectOperation operation,
        CancellationToken callerCancellationToken,
        Func<CancellationToken, Task<object>> operationFactory)
    {
        await operation.WaitForSettlementAsync(callerCancellationToken).ConfigureAwait(false);
        return await RunSingleFlightAsync(processId, callerCancellationToken, operationFactory).ConfigureAwait(false);
    }

    private readonly record struct ConnectOperationKey(SessionManager SessionManager, int ProcessId);

    private sealed class InflightConnectOperation : IDisposable
    {
        private readonly TaskCompletionSource<object> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cancellationSource = new();
        private readonly object _waiterLock = new();
        private int _activeWaiters;
        private bool _acceptingWaiters = true;

        public CancellationToken CancellationToken => _cancellationSource.Token;

        public void AddWaiter()
        {
            lock (_waiterLock)
            {
                _activeWaiters++;
            }
        }

        public bool TryAddWaiter()
        {
            lock (_waiterLock)
            {
                if (!_acceptingWaiters)
                {
                    return false;
                }

                _activeWaiters++;
                return true;
            }
        }

        public Task<object> WaitAsync(CancellationToken callerCancellationToken)
        {
            if (!callerCancellationToken.CanBeCanceled)
            {
                return WaitAndDecrementAsync();
            }

            return WaitWithCallerCancellationAsync(callerCancellationToken);
        }

        private async Task<object> WaitAndDecrementAsync()
        {
            try
            {
                return await _completion.Task.ConfigureAwait(false);
            }
            finally
            {
                RemoveWaiter();
            }
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

        public async Task WaitForSettlementAsync(CancellationToken callerCancellationToken)
        {
            try
            {
                await _completion.Task.WaitAsync(callerCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        private async Task<object> WaitWithCallerCancellationAsync(CancellationToken callerCancellationToken)
        {
            var decrementedInCatch = false;
            try
            {
                return await _completion.Task.WaitAsync(callerCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
            {
                decrementedInCatch = true;
                var lastWaiterCancelled = RemoveWaiterAndCloseIfLast();
                if (lastWaiterCancelled)
                {
                    try
                    {
                        _cancellationSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                throw;
            }
            finally
            {
                if (!decrementedInCatch)
                {
                    RemoveWaiter();
                }
            }
        }

        private void RemoveWaiter()
        {
            lock (_waiterLock)
            {
                _activeWaiters--;
            }
        }

        private bool RemoveWaiterAndCloseIfLast()
        {
            lock (_waiterLock)
            {
                _activeWaiters--;
                if (!_completion.Task.IsCompleted && _activeWaiters == 0)
                {
                    _acceptingWaiters = false;
                    return true;
                }

                return false;
            }
        }
    }
}