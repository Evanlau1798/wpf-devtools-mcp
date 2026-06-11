namespace WpfDevTools.Inspector.Analyzers;

internal static class DispatcherRequestContext
{
    private static readonly AsyncLocal<CancellationToken?> CurrentToken = new();

    public static CancellationToken CancellationToken => CurrentToken.Value ?? CancellationToken.None;

    public static IDisposable Push(CancellationToken cancellationToken)
    {
        var previous = CurrentToken.Value;
        CurrentToken.Value = cancellationToken;
        return new Scope(previous);
    }

    private sealed class Scope(CancellationToken? previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentToken.Value = previous;
        }
    }
}
