using System.Threading;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private static readonly AsyncLocal<Func<Task>?> BeforeSingleFlightCompletionForTestingValue = new();

    internal static Func<Task>? BeforeSingleFlightCompletionForTesting
    {
        get => BeforeSingleFlightCompletionForTestingValue.Value;
        set => BeforeSingleFlightCompletionForTestingValue.Value = value;
    }

    /// <summary>
    /// Runs a connect attempt as a shared in-flight operation for the same SessionManager and processId.
    /// </summary>
    /// <remarks>
    /// Concurrent callers for the same SessionManager and processId share the in-flight connect operation
    /// and receive its result or exception instead of starting duplicate injection attempts.
    /// Caller cancellation removes only that waiter while other waiters keep the operation alive; if the
    /// last waiter cancels, the shared operation is cancelled. Completed single-flight operations are removed and are not cached; later
    /// calls either return AlreadyConnected through the existing-session path before this helper or start a
    /// fresh connect attempt. Callers arriving after an operation is closed to new waiters wait for settlement
    /// and retry.
    /// </remarks>
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

            operation.Dispose();
        }
    }

    private async Task CompleteSingleFlightAsync(
        ConnectOperationKey operationKey,
        InflightConnectOperation operation,
        Func<CancellationToken, Task<object>> operationFactory)
    {
        object? result = null;
        Exception? exception = null;
        try
        {
            result = await operationFactory(operation.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        finally
        {
            operation.StopAcceptingWaiters();
            GlobalInflightConnects.TryRemove(new KeyValuePair<ConnectOperationKey, InflightConnectOperation>(operationKey, operation));
        }

        try
        {
            var beforeCompletion = BeforeSingleFlightCompletionForTesting;
            if (beforeCompletion is not null)
            {
                try
                {
                    await beforeCompletion().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }

            if (exception is not null)
            {
                operation.SetException(exception);
            }
            else
            {
                operation.SetResult(result!);
            }
        }
        finally
        {
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

        public void StopAcceptingWaiters()
        {
            lock (_waiterLock)
            {
                _acceptingWaiters = false;
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