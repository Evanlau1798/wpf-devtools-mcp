using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Performance metrics
///
/// DESIGN NOTE - Static Mutable State:
/// This class intentionally uses static mutable state for performance monitoring.
/// Rationale:
/// 1. WPF rendering is global per-application (single UI thread, single rendering pipeline)
/// 2. CompositionTarget.Rendering is a static event that fires 60 times/second
/// 3. Replacing collections on every frame (immutable pattern) would cause excessive GC pressure
/// 4. Multiple analyzer instances should share the same monitoring session
///
/// Thread Safety: All static state is protected by locks (_lock, _bindingLock)
/// </summary>
public sealed class PerformanceAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    internal PerformanceAnalyzer() : this(new ElementFinder())
    {
    }

    /// <summary>
    /// Create a new PerformanceAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public PerformanceAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    // Static state for global WPF rendering monitoring
    // Protected by _lock for thread safety
    private static readonly object _lock = new object();
    private static bool _isMonitoring = false;
    private static Stopwatch _frameStopwatch = new Stopwatch();
    private static readonly CircularBuffer<double> _frameTimes = new CircularBuffer<double>(MaxFrameSamples);
    private static int _frameCount = 0;
    private static DateTime _monitoringStartTime;

    // CRITICAL FIX: Static constructor to register cleanup on AppDomain unload
    static PerformanceAnalyzer()
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) => StopMonitoring();
        AppDomain.CurrentDomain.DomainUnload += (s, e) => StopMonitoring();
    }

    // Keep last 60 frames (1 second at 60 FPS)
    private const int MaxFrameSamples = 60;

    private static readonly List<WeakReference> _bindingReferences = new List<WeakReference>();
    private static readonly object _bindingLock = new object();

    /// <summary>
    /// Get render statistics
    /// </summary>
    public object GetRenderStats()
    {
        // Start monitoring on UI thread
        InvokeOnUIThread(() => EnsureMonitoringStarted());

        // Brief warm-up: allow rendering events to populate frame data.
        // This runs on a non-UI thread so the dispatcher can process
        // CompositionTarget.Rendering callbacks that write frame data.
        var warmupStart = DateTime.UtcNow;
        while (_frameTimes.Count == 0 && (DateTime.UtcNow - warmupStart).TotalMilliseconds < 250)
        {
            Thread.Sleep(20);
        }

        // Read stats on UI thread
        return InvokeOnUIThread<object>(() =>
        {
            lock (_lock)
            {
                if (_frameTimes.Count == 0)
                {
                    var (warmupConfidence, warmupGuidance) = PerformanceConfidencePolicy.EvaluateRenderStats(sampleCount: 0, isWarmedUp: false);
                    return new
                    {
                        success = true,
                        message = "Monitoring started, waiting for frame data...",
                        isWarmedUp = false,
                        confidence = warmupConfidence,
                        minimumRecommendedSampleCount = PerformanceConfidencePolicy.MinRenderSampleCount,
                        minimumRecommendedMonitoringDurationMs = PerformanceConfidencePolicy.MinRenderMonitoringDurationMs,
                        sampleGuidance = warmupGuidance,
                        sampleCount = 0,
                        sampleWindowSize = MaxFrameSamples,
                        frameRate = 0.0,
                        avgRenderTime = 0.0,
                        averageFrameTime = 0.0,
                        dirtyRegionCount = 0,
                        minFrameTime = 0.0,
                        maxFrameTime = 0.0,
                        totalFrames = _frameCount,
                        monitoringDuration = 0.0,
                        visualCount = GetVisualCountInternal()
                    };
                }

                var frameTimesArray = _frameTimes.GetItems().ToArray();
                var avgFrameTime = frameTimesArray.Average();
                var frameRate = avgFrameTime > 0 ? 1000.0 / avgFrameTime : 0.0;
                var monitoringDuration = (DateTime.UtcNow - _monitoringStartTime).TotalSeconds;
                var (statsConfidence, statsGuidance) = PerformanceConfidencePolicy.EvaluateRenderStats(_frameTimes.Count, isWarmedUp: true);

                return new
                {
                    success = true,
                    isWarmedUp = true,
                    confidence = statsConfidence,
                    minimumRecommendedSampleCount = PerformanceConfidencePolicy.MinRenderSampleCount,
                    minimumRecommendedMonitoringDurationMs = PerformanceConfidencePolicy.MinRenderMonitoringDurationMs,
                    sampleGuidance = statsGuidance,
                    sampleCount = _frameTimes.Count,
                    sampleWindowSize = MaxFrameSamples,
                    frameRate = Math.Round(frameRate, 2),
                    avgRenderTime = Math.Round(avgFrameTime, 2),
                    averageFrameTime = Math.Round(avgFrameTime, 2),
                    dirtyRegionCount = 0,
                    minFrameTime = Math.Round(frameTimesArray.Min(), 2),
                    maxFrameTime = Math.Round(frameTimesArray.Max(), 2),
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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var count = CountVisualElements(element);

            return new
            {
                success = true,
                count = count,
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
                return ToolErrorFactory.ElementNotFound(elementId);
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
                renderTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                renderTime = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                message = "Approximate render time (includes layout update)",
                confidence = "low",
                recommendedSampleCount = PerformanceConfidencePolicy.RecommendedRenderMeasurementSamples,
                sampleGuidance = $"Single-shot timing is noisy; run {PerformanceConfidencePolicy.RecommendedRenderMeasurementSamples}+ samples and compare median/p95.",
                elementType = element.GetType().Name
            };
        });
    }

    /// <summary>
    /// Find binding leaks
    /// </summary>
    public object FindBindingLeaks(int threshold = 100, int? samplingDurationMs = null)
    {
        var effectiveSamplingDurationMs = Math.Max(0, samplingDurationMs ?? 0);

        if (effectiveSamplingDurationMs > 0)
        {
            Thread.Sleep(Math.Min(effectiveSamplingDurationMs, 15000));
        }

        // Only force GC when off the UI thread to avoid blocking rendering
        if (Application.Current?.Dispatcher.CheckAccess() != true)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

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
                var suspects = potentialLeaks
                    .Select(item =>
                    {
                        var typeProperty = item.GetType().GetProperty("type");
                        return new
                        {
                            elementId = (string?)null,
                            elementType = typeProperty?.GetValue(item)?.ToString(),
                            bindingCount = 1
                        };
                    })
                    .ToList();
                var (confidence, guidance) = PerformanceConfidencePolicy.EvaluateBindingLeakSampling(
                    effectiveSamplingDurationMs,
                    _bindingReferences.Count);

                return new
                {
                    success = true,
                    totalTracked = _bindingReferences.Count,
                    aliveBindings = aliveBindings.Count,
                    deadBindings = deadCount,
                    threshold,
                    hasLeaks,
                    potentialLeaks,
                    suspects,
                    confidence,
                    samplingDurationMs = effectiveSamplingDurationMs,
                    minimumRecommendedSamplingDurationMs = PerformanceConfidencePolicy.MinBindingLeakSamplingDurationMs,
                    sampleGuidance = guidance,
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
                _monitoringStartTime = DateTime.UtcNow;
                _frameStopwatch.Start();
                CompositionTarget.Rendering += OnRendering;
            }
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        // IMPORTANT FIX: Use 1ms timeout instead of immediate skip
        // This gives a brief window to acquire the lock without blocking rendering
        // 1ms is acceptable overhead for 60 FPS rendering (16.67ms per frame)
        if (!System.Threading.Monitor.TryEnter(_lock, TimeSpan.FromMilliseconds(1)))
            return;

        try
        {
            _frameCount++;

            if (_frameStopwatch.IsRunning)
            {
                var frameTime = _frameStopwatch.Elapsed.TotalMilliseconds;
                _frameStopwatch.Restart();

                // Add frame time to circular buffer (O(1) operation)
                // Automatically overwrites oldest when full - no RemoveAt(0) needed
                _frameTimes.Add(frameTime);
            }
        }
        finally
        {
            System.Threading.Monitor.Exit(_lock);
        }
    }

    private int GetVisualCountInternal()
    {
        var root = GetRootElement();
        return root != null ? CountVisualElements(root) : 0;
    }

    private int CountVisualElements(DependencyObject element, int maxDepth = 100, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth)
            return 1;

        int count = 1; // Count the element itself

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            count += CountVisualElements(child, maxDepth, currentDepth + 1);
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
            _monitoringStartTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Reset all monitoring state and clear tracked resources
    /// Call this when disconnecting from a process to prevent memory leaks
    /// </summary>
    public static void ResetMonitoring()
    {
        lock (_lock)
        {
            if (_isMonitoring)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isMonitoring = false;
            }

            _frameTimes.Clear();
            _frameCount = 0;
            _frameStopwatch.Stop();
            _frameStopwatch.Reset();
        }

        lock (_bindingLock)
        {
            _bindingReferences.Clear();
        }
    }
}
