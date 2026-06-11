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
    private readonly Func<IEnumerable<string>> _enumeratePipeNames;

    private const int PollingIntervalMs = 100;

    /// <summary>
    /// Create a PipeReadyProbe with injectable dependencies for testing.
    /// </summary>
    public PipeReadyProbe(
        Func<string, uint, bool> waitNamedPipe,
        Func<DateTime> utcNow,
        Action<int> sleep,
        Func<IEnumerable<string>>? enumeratePipeNames = null)
    {
        _waitNamedPipe = waitNamedPipe ?? throw new ArgumentNullException(nameof(waitNamedPipe));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _sleep = sleep ?? throw new ArgumentNullException(nameof(sleep));
        _enumeratePipeNames = enumeratePipeNames ?? EnumerateSystemPipeNames;
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
        var deadline = _utcNow() + timeout;

        while (_utcNow() < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (IsPipeReady(pipeName))
            {
                return true;
            }

            _sleep(PollingIntervalMs);
        }

        return false;
    }

    /// <summary>
    /// Poll for a ready pipe with the exact name or a randomized suffix after the prefix.
    /// </summary>
    /// <param name="pipeNamePrefix">Pipe prefix without path, for example "WpfDevTools_1234".</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="pipeName">The ready pipe name without path when found.</param>
    public bool TryFindReadyPipeByPrefix(
        string pipeNamePrefix,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        out string? pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeNamePrefix))
        {
            throw new ArgumentException("Pipe name prefix is required.", nameof(pipeNamePrefix));
        }

        var deadline = _utcNow() + timeout;

        while (_utcNow() < deadline && !cancellationToken.IsCancellationRequested)
        {
            foreach (var candidatePipeName in EnumerateCandidatePipeNames(pipeNamePrefix))
            {
                if (IsPipeReady(candidatePipeName))
                {
                    pipeName = candidatePipeName;
                    return true;
                }
            }

            _sleep(PollingIntervalMs);
        }

        pipeName = null;
        return false;
    }

    private bool IsPipeReady(string pipeName)
    {
        var fullPipeName = $@"\\.\pipe\{pipeName}";
        return _waitNamedPipe(fullPipeName, 0);
    }

    private IEnumerable<string> EnumerateCandidatePipeNames(string pipeNamePrefix)
    {
        yield return pipeNamePrefix;

        foreach (var pipeName in _enumeratePipeNames())
        {
            var candidatePipeName = Path.GetFileName(pipeName);
            if (!string.IsNullOrWhiteSpace(candidatePipeName) &&
                candidatePipeName.StartsWith(pipeNamePrefix + "_", StringComparison.Ordinal))
            {
                yield return candidatePipeName;
            }
        }
    }

    private static IReadOnlyList<string> EnumerateSystemPipeNames()
    {
        try
        {
            return Directory.EnumerateFiles(@"\\.\pipe\")
                .Select(Path.GetFileName)
                .Where(pipeName => !string.IsNullOrWhiteSpace(pipeName))
                .Cast<string>()
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeWaitNamedPipe(string name, uint timeout);
}
