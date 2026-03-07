using System.Runtime.InteropServices;

namespace WpfDevTools.Injector;

/// <summary>
/// Non-consuming pipe availability probe using WaitNamedPipe.
/// Does NOT establish a connection — only checks if a pipe instance exists.
/// Injectable dependencies for testability.
/// </summary>
public sealed class PipeReadyProbe
{
    private readonly Func<string, uint, bool> _waitNamedPipe;
    private readonly Func<DateTime> _utcNow;
    private readonly Action<int> _sleep;

    private const int PollingIntervalMs = 100;

    /// <summary>
    /// Create a PipeReadyProbe with injectable dependencies for testing.
    /// </summary>
    public PipeReadyProbe(
        Func<string, uint, bool> waitNamedPipe,
        Func<DateTime> utcNow,
        Action<int> sleep)
    {
        _waitNamedPipe = waitNamedPipe ?? throw new ArgumentNullException(nameof(waitNamedPipe));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _sleep = sleep ?? throw new ArgumentNullException(nameof(sleep));
    }

    /// <summary>
    /// Create a PipeReadyProbe with real Win32 WaitNamedPipe.
    /// </summary>
    public PipeReadyProbe()
        : this(NativeWaitNamedPipe, () => DateTime.UtcNow, Thread.Sleep)
    {
    }

    /// <summary>
    /// Poll for Named Pipe availability (non-consuming).
    /// Returns true if pipe exists and server is listening, false on timeout/cancel.
    /// </summary>
    /// <param name="pipeName">Pipe name without prefix (e.g., "WpfDevTools_1234")</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public bool WaitForPipeReady(
        string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var fullPipeName = $@"\\.\pipe\{pipeName}";
        var deadline = _utcNow() + timeout;

        while (_utcNow() < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (_waitNamedPipe(fullPipeName, 0))
            {
                return true;
            }

            _sleep(PollingIntervalMs);
        }

        return false;
    }

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeWaitNamedPipe(string name, uint timeout);
}
