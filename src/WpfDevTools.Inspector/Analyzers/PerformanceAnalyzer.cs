using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Performance metrics
/// </summary>
public class PerformanceAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    public PerformanceAnalyzer() : this(new ElementFinder())
    {
    }

    public PerformanceAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    private static readonly object _lock = new object();
    private static bool _isMonitoring = false;
    private static Stopwatch _frameStopwatch = new Stopwatch();
    private static readonly List<double> _frameTimes = new List<double>();
    private static int _frameCount = 0;
    private static DateTime _monitoringStartTime;

    // Keep last 60 frames (1 second at 60 FPS)
    private const int MaxFrameSamples = 60;

    private static readonly List<WeakReference> _bindingReferences = new List<WeakReference>();
    private static readonly object _bindingLock = new object();

    /// <summary>
    /// Get render statistics
    /// </summary>
    public object GetRenderStats()
    {
        return InvokeOnUIThread<object>(() =>
        {
            // Start monitoring if not already started
            EnsureMonitoringStarted();

            lock (_lock)
            {
                if (_frameTimes.Count == 0)
                {
                    return new
                    {
                        success = true,
                        message = "Monitoring started, waiting for frame data...",
                        frameRate = 0.0,
                        averageFrameTime = 0.0,
                        minFrameTime = 0.0,
                        maxFrameTime = 0.0,
                        totalFrames = _frameCount,
                        monitoringDuration = 0.0,
                        visualCount = GetVisualCountInternal()
                    };
                }

                var avgFrameTime = _frameTimes.Average();
                var frameRate = avgFrameTime > 0 ? 1000.0 / avgFrameTime : 0.0;
                var monitoringDuration = (DateTime.Now - _monitoringStartTime).TotalSeconds;

                return new
                {
                    success = true,
                    frameRate = Math.Round(frameRate, 2),
                    averageFrameTime = Math.Round(avgFrameTime, 2),
                    minFrameTime = Math.Round(_frameTimes.Min(), 2),
                    maxFrameTime = Math.Round(_frameTimes.Max(), 2),
                    totalFrames = _frameCount,
                    monitoringDuration = Math.Round(monitoringDuration, 2),
                    visualCount = GetVisualCountInternal()
                };
            }
        });
    }

    /// <summary>
    /// Get visual element count
    /// </summary>
    public object GetVisualCount(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? GetRootElement()
                : FindElementById(elementId);

            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            var count = CountVisualElements(element);

            return new
            {
                success = true,
                totalCount = count,
                elementType = element.GetType().Name
            };
        });
    }

    /// <summary>
    /// Measure element render time
    /// </summary>
    public object MeasureElementRenderTime(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? GetRootElement()
                : FindElementById(elementId);

            if (element == null)
            {
                return new { success = false, error = "Element not found" };
            }

            // Approximate render time by forcing invalidation and measuring next frame
            var sw = Stopwatch.StartNew();

            if (element is UIElement uiElement)
            {
                uiElement.InvalidateVisual();
                uiElement.UpdateLayout();
            }

            sw.Stop();

            return new
            {
                success = true,
                renderTime = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                message = "Approximate render time (includes layout update)",
                elementType = element.GetType().Name
            };
        });
    }

    /// <summary>
    /// Find binding leaks
    /// </summary>
    public object FindBindingLeaks(int threshold = 100)
    {
        // Force garbage collection OUTSIDE the UI thread to avoid blocking rendering
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return InvokeOnUIThread<object>(() =>
        {
            lock (_bindingLock)
            {
                // Check which bindings are still alive
                var aliveBindings = new List<object>();
                var deadCount = 0;

                foreach (var weakRef in _bindingReferences.ToList())
                {
                    if (weakRef.IsAlive && weakRef.Target != null)
                    {
                        var target = weakRef.Target;
                        aliveBindings.Add(new
                        {
                            type = target.GetType().Name,
                            hashCode = target.GetHashCode(),
                            toString = target.ToString()
                        });
                    }
                    else
                    {
                        deadCount++;
                    }
                }

                // Clean up dead references
                _bindingReferences.RemoveAll(wr => !wr.IsAlive);

                var hasLeaks = aliveBindings.Count > threshold;
                var potentialLeaks = hasLeaks
                    ? aliveBindings.Take(10).ToList()
                    : new List<object>();

                return new
                {
                    success = true,
                    totalTracked = _bindingReferences.Count,
                    aliveBindings = aliveBindings.Count,
                    deadBindings = deadCount,
                    threshold,
                    hasLeaks,
                    potentialLeaks,
                    message = hasLeaks
                        ? $"Potential memory leak detected: {aliveBindings.Count} bindings alive (threshold: {threshold})"
                        : $"No binding leaks detected ({aliveBindings.Count} bindings alive, threshold: {threshold})",
                    recommendation = hasLeaks
                        ? "Consider checking for event handler leaks, circular references, or bindings to static objects"
                        : "Binding memory usage appears normal"
                };
            }
        });
    }

    private const int MaxBindingReferences = 10000;

    /// <summary>
    /// Track a binding for leak detection
    /// </summary>
    public static void TrackBinding(object binding)
    {
        lock (_bindingLock)
        {
            _bindingReferences.Add(new WeakReference(binding));

            // Trim oldest entries if exceeding capacity to prevent unbounded growth
            if (_bindingReferences.Count > MaxBindingReferences)
            {
                _bindingReferences.RemoveRange(0, _bindingReferences.Count - MaxBindingReferences);
            }
        }
    }

    /// <summary>
    /// Clear all tracked bindings
    /// </summary>
    public static void ClearTrackedBindings()
    {
        lock (_bindingLock)
        {
            _bindingReferences.Clear();
        }
    }

    private static void EnsureMonitoringStarted()
    {
        lock (_lock)
        {
            if (!_isMonitoring)
            {
                _isMonitoring = true;
                _monitoringStartTime = DateTime.Now;
                _frameStopwatch.Start();
                CompositionTarget.Rendering += OnRendering;
            }
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            _frameCount++;

            if (_frameStopwatch.IsRunning)
            {
                var frameTime = _frameStopwatch.Elapsed.TotalMilliseconds;
                _frameStopwatch.Restart();

                // Add frame time to list
                _frameTimes.Add(frameTime);

                // Keep only last N samples
                if (_frameTimes.Count > MaxFrameSamples)
                {
                    _frameTimes.RemoveAt(0);
                }
            }
        }
    }

    private int GetVisualCountInternal()
    {
        var root = GetRootElement();
        return root != null ? CountVisualElements(root) : 0;
    }

    private int CountVisualElements(DependencyObject element)
    {
        int count = 1; // Count the element itself

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            count += CountVisualElements(child);
        }

        return count;
    }

    private DependencyObject? GetRootElement()
    {
        return _elementFinder.GetRootElement();
    }

    private DependencyObject? FindElementById(string elementId)
    {
        return _elementFinder.FindById(elementId);
    }

    /// <summary>
    /// Stop performance monitoring
    /// </summary>
    public static void StopMonitoring()
    {
        lock (_lock)
        {
            if (_isMonitoring)
            {
                CompositionTarget.Rendering -= OnRendering;
                _frameStopwatch.Stop();
                _isMonitoring = false;
            }
        }
    }

    /// <summary>
    /// Reset performance statistics
    /// </summary>
    public static void ResetStatistics()
    {
        lock (_lock)
        {
            _frameTimes.Clear();
            _frameCount = 0;
            _frameStopwatch.Reset();
            _monitoringStartTime = DateTime.Now;
        }
    }
}
