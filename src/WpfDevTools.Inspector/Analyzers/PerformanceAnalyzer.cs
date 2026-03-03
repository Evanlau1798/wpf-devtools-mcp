using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Performance metrics
/// </summary>
public class PerformanceAnalyzer
{
    private static readonly object _lock = new object();
    private static bool _isMonitoring = false;
    private static Stopwatch _frameStopwatch = new Stopwatch();
    private static List<double> _frameTimes = new List<double>();
    private static int _frameCount = 0;
    private static DateTime _monitoringStartTime;
    private const int MaxFrameSamples = 60; // Keep last 60 frames

    /// <summary>
    /// Get render statistics
    /// </summary>
    public object GetRenderStats()
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetRenderStats());
        }

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
    }

    /// <summary>
    /// Get visual element count
    /// </summary>
    public object GetVisualCount(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetVisualCount(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        var count = CountVisualElements(element);

        return new
        {
            totalCount = count,
            elementType = element.GetType().Name
        };
    }

    /// <summary>
    /// Measure element render time
    /// </summary>
    public object MeasureElementRenderTime(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => MeasureElementRenderTime(elementId));
        }

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
    }

    /// <summary>
    /// Find binding leaks
    /// </summary>
    public object FindBindingLeaks(int threshold = 100)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => FindBindingLeaks(threshold));
        }

        // TODO: Implement binding leak detection
        // This requires tracking binding creation and detecting memory leaks
        // Possible approaches:
        // 1. Track WeakReference to bindings and check if they're still alive
        // 2. Monitor binding creation/disposal events
        // 3. Analyze binding expressions for potential leaks (e.g., event handlers)
        // 4. Use memory profiling to detect objects that should be GC'd but aren't

        return new
        {
            success = true,
            message = "Binding leak detection not yet implemented",
            threshold = threshold,
            leaks = new object[] { }
        };
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
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        // This should search the visual tree for an element with the given name or ID
        return GetRootElement();
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
