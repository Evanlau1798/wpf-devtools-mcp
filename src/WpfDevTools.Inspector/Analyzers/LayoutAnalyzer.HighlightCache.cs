using System.Collections.Concurrent;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class LayoutAnalyzer
{
    private const int MaxTrackedHighlights = 128;
    private static readonly TimeSpan HighlightCleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxHighlightRetention = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, HighlightEntry> _highlights = new();
    private static readonly Timer _highlightCleanupTimer = CreateHighlightCleanupTimer();

    internal static int MaxTrackedHighlightsForTests => MaxTrackedHighlights;

    internal static bool IsHighlightCleanupTimerActiveForTests => _highlightCleanupTimer != null;

    internal static int GetTrackedHighlightCountForTests()
    {
        return _highlights.Count;
    }

    internal static IReadOnlyCollection<string> GetTrackedHighlightKeysForTests()
    {
        return _highlights.Keys.ToArray();
    }

    internal static TimeSpan GetEffectiveHighlightDurationForTests(int duration)
    {
        return GetEffectiveHighlightDuration(duration);
    }

    internal static Action RegisterHighlightForTests(
        string key,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        Action? remove = null)
    {
        var entry = new HighlightEntry(createdAtUtc, expiresAtUtc, remove ?? (() => { }));
        RegisterHighlight(key, entry);

        return () => RemoveHighlight(key, entry);
    }

    internal static void TrackHighlightForTests(
        string key,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        Action? remove = null)
    {
        _highlights[key] = new HighlightEntry(createdAtUtc, expiresAtUtc, remove ?? (() => { }));
    }

    internal static void CleanupHighlightsForTests(DateTimeOffset nowUtc)
    {
        CleanupHighlights(nowUtc);
    }

    internal static void ClearHighlightsForTests()
    {
        foreach (var key in _highlights.Keys.ToList())
        {
            RemoveHighlight(key);
        }

        _highlights.Clear();
    }

    private static Timer CreateHighlightCleanupTimer()
    {
        return new Timer(
            _ => CleanupHighlights(DateTimeOffset.UtcNow),
            null,
            HighlightCleanupInterval,
            HighlightCleanupInterval);
    }

    private static void RegisterHighlight(string key, HighlightEntry entry)
    {
        RemoveHighlight(key);
        _highlights[key] = entry;
        CleanupHighlights(DateTimeOffset.UtcNow);
    }

    private static void ScheduleHighlightRemoval(string key, HighlightEntry entry, TimeSpan duration)
    {
        _ = Task.Delay(duration).ContinueWith(
            _ => RemoveHighlight(key, entry),
            TaskScheduler.Default);
    }

    private static TimeSpan GetEffectiveHighlightDuration(int duration)
    {
        var retentionLimitMs = (int)MaxHighlightRetention.TotalMilliseconds;
        var effectiveDurationMs = Math.Min(Math.Max(0, duration), retentionLimitMs);
        return TimeSpan.FromMilliseconds(effectiveDurationMs);
    }

    private static void CleanupHighlights(DateTimeOffset nowUtc)
    {
        var expiredKeys = _highlights
            .Where(item => item.Value.IsExpired(nowUtc))
            .Select(item => item.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            RemoveHighlight(key);
        }

        var overflow = _highlights.Count - MaxTrackedHighlights;
        if (overflow <= 0)
        {
            return;
        }

        var oldestKeys = _highlights
            .OrderBy(item => item.Value.CreatedAtUtc)
            .Take(overflow)
            .Select(item => item.Key)
            .ToList();

        foreach (var key in oldestKeys)
        {
            RemoveHighlight(key);
        }
    }

    private static void RemoveHighlight(string key)
    {
        if (_highlights.TryRemove(key, out var entry))
        {
            entry.Remove();
        }
    }

    private static void RemoveHighlight(string key, HighlightEntry entry)
    {
        var removed = ((ICollection<KeyValuePair<string, HighlightEntry>>)_highlights)
            .Remove(new KeyValuePair<string, HighlightEntry>(key, entry));

        if (removed)
        {
            entry.Remove();
        }
    }

    private static Action CreateHighlightRemoval(AdornerLayer adornerLayer, HighlightAdorner adorner)
    {
        return () =>
        {
            try
            {
                var dispatcher = adornerLayer.Dispatcher;
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return;
                }

                if (dispatcher.CheckAccess())
                {
                    adornerLayer.Remove(adorner);
                    return;
                }

                _ = dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                        {
                            adornerLayer.Remove(adorner);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    private static Action CreateHighlightRemoval(Popup popup)
    {
        return () =>
        {
            try
            {
                var dispatcher = popup.Dispatcher;
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return;
                }

                if (dispatcher.CheckAccess())
                {
                    ClosePopupHighlight(popup);
                    return;
                }

                _ = dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                        {
                            ClosePopupHighlight(popup);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    private static void ClosePopupHighlight(Popup popup)
    {
        popup.IsOpen = false;
        popup.Child = null;
    }

    private sealed class HighlightEntry
    {
        private readonly Action _remove;
        private int _removed;

        public HighlightEntry(DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc, Action remove)
        {
            CreatedAtUtc = createdAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            _remove = remove;
        }

        public DateTimeOffset CreatedAtUtc { get; }

        private DateTimeOffset ExpiresAtUtc { get; }

        public bool IsExpired(DateTimeOffset nowUtc)
        {
            return nowUtc >= ExpiresAtUtc || nowUtc - CreatedAtUtc >= MaxHighlightRetention;
        }

        public void Remove()
        {
            if (Interlocked.Exchange(ref _removed, 1) == 0)
            {
                _remove();
            }
        }
    }
}
