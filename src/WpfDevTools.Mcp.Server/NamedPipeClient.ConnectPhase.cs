namespace WpfDevTools.Mcp.Server;

public sealed partial class NamedPipeClient
{
    internal static async Task WaitForConnectPhaseAsync(
        Task operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.IsCompleted)
        {
            await operation.ConfigureAwait(false);
            return;
        }

        var cancellationSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationSignal);
        var completed = await Task.WhenAny(operation, cancellationSignal.Task).ConfigureAwait(false);
        if (!ReferenceEquals(completed, operation))
        {
            ObserveFaults(operation);
            throw new OperationCanceledException(cancellationToken);
        }

        await operation.ConfigureAwait(false);
    }

    internal static async Task<T> WaitForConnectPhaseAsync<T>(
        Task<T> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.IsCompleted)
        {
            return await operation.ConfigureAwait(false);
        }

        var cancellationSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationSignal);
        var completed = await Task.WhenAny(operation, cancellationSignal.Task).ConfigureAwait(false);
        if (!ReferenceEquals(completed, operation))
        {
            ObserveFaults(operation);
            throw new OperationCanceledException(cancellationToken);
        }

        return await operation.ConfigureAwait(false);
    }

    private static void ObserveFaults(Task operation)
    {
        _ = operation.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
