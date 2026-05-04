using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Configuration;

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
public sealed partial class PerformanceAnalyzer : DispatcherAnalyzerBase
{
    private static readonly ElementFinder SharedElementFinder = new ElementFinder();
    private static int _sharedElementFinderDisposed = 0;
    private readonly ElementFinder _elementFinder;

    internal PerformanceAnalyzer() : this(SharedElementFinder)
    {
    }

    /// <summary>
    /// Create a new PerformanceAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public PerformanceAnalyzer(ElementFinder elementFinder)
        : base(elementFinder)
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
        AppDomain.CurrentDomain.ProcessExit += (s, e) => DisposeSharedResources();
        AppDomain.CurrentDomain.DomainUnload += (s, e) => DisposeSharedResources();
    }

    // Keep last 60 frames (1 second at 60 FPS)
    private const int MaxFrameSamples = 60;

    private static readonly List<WeakReference> _bindingReferences = new List<WeakReference>();
    private static readonly object _bindingLock = new object();
    private static int _forcedGcPathExecutionCount = 0;

    /// <summary>
    /// Get render statistics
    /// </summary>
    public object GetRenderStats(bool warmUp = false)
    {
        // Start monitoring on UI thread
        InvokeOnUIThread(() => EnsureMonitoringStarted());
        WaitForRenderWarmUp(warmUp);

        // Read stats on UI thread
        return InvokeOnUIThread<object>(() =>
        {
            lock (_lock)
            {
                if (_frameTimes.Count == 0)
                {
                    var (warmupConfidence, warmupGuidance) = PerformanceConfidencePolicy.EvaluateRenderStats(sampleCount: 0, isWarmedUp: false);
                    var warmupVisualCount = GetVisualCountInternal();
                    return new
                    {
                        success = true,
                        message = "Monitoring started, waiting for frame data...",
                        isWarmedUp = false,
                        confidence = warmupConfidence,
                        warmUpApplied = warmUp,
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
                        visualCount = warmupVisualCount.Count,
                        visualCountLimit = warmupVisualCount.Limit,
                        visualCountTruncated = warmupVisualCount.Truncated
                    };
                }

                var frameTimesArray = _frameTimes.GetItems().ToArray();
                var avgFrameTime = frameTimesArray.Average();
                var frameRate = avgFrameTime > 0 ? 1000.0 / avgFrameTime : 0.0;
                var monitoringDuration = (DateTime.UtcNow - _monitoringStartTime).TotalSeconds;
                var (statsConfidence, statsGuidance) = PerformanceConfidencePolicy.EvaluateRenderStats(_frameTimes.Count, isWarmedUp: true);

                var statsVisualCount = GetVisualCountInternal();
                return new
                {
                    success = true,
                    isWarmedUp = true,
                    confidence = statsConfidence,
                    warmUpApplied = warmUp,
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
                    visualCount = statsVisualCount.Count,
                    visualCountLimit = statsVisualCount.Limit,
                    visualCountTruncated = statsVisualCount.Truncated
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
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var visualCount = CountVisualElements(element);

            return new
            {
                success = true,
                count = visualCount.Count,
                totalCount = visualCount.Count,
                visualCountLimit = visualCount.Limit,
                visualCountTruncated = visualCount.Truncated,
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
            var element = ResolveElement(elementId);

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
    public async Task<object> FindBindingLeaksAsync(
        int threshold = 100,
        int? samplingDurationMs = null,
        bool warmUp = false,
        CancellationToken cancellationToken = default)
    {
        var effectiveSamplingDurationMs = GetEffectiveBindingLeakSamplingDuration(samplingDurationMs, warmUp);

        if (effectiveSamplingDurationMs > 0)
        {
            await Task.Delay(Math.Min(effectiveSamplingDurationMs, 15000), cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Only force GC when off the UI thread to avoid blocking rendering
        if (Application.Current?.Dispatcher.CheckAccess() != true)
        {
            Interlocked.Increment(ref _forcedGcPathExecutionCount);
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
                    var target = weakRef.Target;
                    if (target != null)
                    {
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

                var observedTrackedCount = aliveBindings.Count + deadCount;

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
                    observedTrackedCount);

                return new
                {
                    success = true,
                    totalTracked = observedTrackedCount,
                    aliveBindings = aliveBindings.Count,
                    deadBindings = deadCount,
                    threshold,
                    hasLeaks,
                    potentialLeaks,
                    suspects,
                    confidence,
                    warmUpApplied = warmUp,
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

    internal static int GetForcedGcPathExecutionCount()
    {
        return Volatile.Read(ref _forcedGcPathExecutionCount);
    }

    internal static void ResetForcedGcPathExecutionCount()
    {
        Interlocked.Exchange(ref _forcedGcPathExecutionCount, 0);
    }

    private static void DisposeSharedResources()
    {
        StopMonitoring();

        if (Interlocked.Exchange(ref _sharedElementFinderDisposed, 1) == 0)
        {
            SharedElementFinder.Dispose();
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

    private VisualCountResult GetVisualCountInternal()
    {
        var root = GetRootElement();
        return root != null
            ? CountVisualElements(root, TreeTraversalDefaults.DefaultMaxNodes)
            : VisualCountResult.Empty;
    }

    private DependencyObject? GetRootElement()
    {
        return _elementFinder.GetRootElement();
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
