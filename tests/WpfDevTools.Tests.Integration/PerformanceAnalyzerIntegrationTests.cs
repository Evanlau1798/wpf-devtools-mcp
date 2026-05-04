using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for PerformanceAnalyzer requiring full WPF Application context
/// These tests verify methods execute without exceptions in real WPF environment
/// </summary>
[Collection("WpfIntegration")]
public class PerformanceAnalyzerIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;

    public PerformanceAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        PerformanceAnalyzer.ResetMonitoring();
        PerformanceAnalyzer.ResetForcedGcPathExecutionCount();
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
            if (Application.Current?.MainWindow is not { } mainWindow)
            {
                return;
            }

            mainWindow.Content = null;
            mainWindow.Hide();
        });

        PerformanceAnalyzer.ResetMonitoring();
        PerformanceAnalyzer.ResetForcedGcPathExecutionCount();
    }

    [Fact]
    public async Task FindBindingLeaksAsync_WithNoTrackedBindings_ShouldExecuteSuccessfully()
    {
        // Arrange
        using var elementFinder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(elementFinder);
        PerformanceAnalyzer.ClearTrackedBindings();

        // Act
        var result = JsonSerializer.SerializeToElement(await analyzer.FindBindingLeaksAsync(100));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("totalTracked").GetInt32().Should().Be(0);
        result.GetProperty("aliveBindings").GetInt32().Should().Be(0);
        result.GetProperty("deadBindings").GetInt32().Should().Be(0);
        result.GetProperty("hasLeaks").GetBoolean().Should().BeFalse();
        result.GetProperty("potentialLeaks").GetArrayLength().Should().Be(0);
        result.GetProperty("suspects").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task FindBindingLeaksAsync_WithTrackedBindings_ShouldExecuteSuccessfully()
    {
        // Arrange
        using var elementFinder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(elementFinder);
        PerformanceAnalyzer.ClearTrackedBindings();
        var trackedBindings = new List<Binding>();

        // Track 5 bindings
        for (int i = 0; i < 5; i++)
        {
            var binding = new Binding($"Property{i}");
            trackedBindings.Add(binding);
            PerformanceAnalyzer.TrackBinding(binding);
        }

        // Act
        var result = JsonSerializer.SerializeToElement(await analyzer.FindBindingLeaksAsync(threshold: 10));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("totalTracked").GetInt32().Should().Be(5);
        result.GetProperty("aliveBindings").GetInt32().Should().Be(5);
        result.GetProperty("deadBindings").GetInt32().Should().Be(0);
        result.GetProperty("hasLeaks").GetBoolean().Should().BeFalse();
        result.GetProperty("potentialLeaks").GetArrayLength().Should().Be(0);
        result.GetProperty("suspects").GetArrayLength().Should().Be(0);
        GC.KeepAlive(trackedBindings);
    }

    [Fact]
    public async Task FindBindingLeaksAsync_WithManyTrackedBindings_ShouldExecuteSuccessfully()
    {
        // Arrange
        using var elementFinder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(elementFinder);
        PerformanceAnalyzer.ClearTrackedBindings();
        var trackedBindings = new List<Binding>();

        // Track 15 bindings
        for (int i = 0; i < 15; i++)
        {
            var binding = new Binding($"Property{i}");
            trackedBindings.Add(binding);
            PerformanceAnalyzer.TrackBinding(binding);
        }

        // Act
        var result = JsonSerializer.SerializeToElement(await analyzer.FindBindingLeaksAsync(threshold: 10));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("totalTracked").GetInt32().Should().Be(15);
        result.GetProperty("aliveBindings").GetInt32().Should().Be(15);
        result.GetProperty("deadBindings").GetInt32().Should().Be(0);
        result.GetProperty("hasLeaks").GetBoolean().Should().BeTrue();
        result.GetProperty("potentialLeaks").GetArrayLength().Should().Be(10);
        result.GetProperty("suspects").GetArrayLength().Should().Be(10);
        GC.KeepAlive(trackedBindings);
    }

    [Fact]
    public async Task FindBindingLeaksAsync_AfterGC_ShouldExecuteSuccessfully()
    {
        // Arrange
        using var elementFinder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(elementFinder);
        PerformanceAnalyzer.ClearTrackedBindings();
        PerformanceAnalyzer.ResetForcedGcPathExecutionCount();

        var weakReferences = TrackTemporaryBindings(10);

        // Act - call off the UI thread so FindBindingLeaks exercises its GC path
        var result = JsonSerializer.SerializeToElement(await analyzer.FindBindingLeaksAsync(0));
        PerformanceAnalyzer.GetForcedGcPathExecutionCount().Should().Be(1);
        var cleanupResult = JsonSerializer.SerializeToElement(await analyzer.FindBindingLeaksAsync(0));

        // Assert
        PerformanceAnalyzer.GetForcedGcPathExecutionCount().Should().Be(2);
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("totalTracked").GetInt32().Should().Be(
            result.GetProperty("aliveBindings").GetInt32() + result.GetProperty("deadBindings").GetInt32());
        result.GetProperty("deadBindings").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("hasLeaks").GetBoolean().Should().BeFalse();
        cleanupResult.GetProperty("deadBindings").GetInt32().Should().Be(0);
        cleanupResult.GetProperty("totalTracked").GetInt32().Should().Be(cleanupResult.GetProperty("aliveBindings").GetInt32());
        weakReferences.Should().Contain(reference => !reference.IsAlive);
    }

    [Fact]
    public void GetRenderStats_ShouldExecuteSuccessfully()
    {
        using var elementFinder = new ElementFinder();
        var analyzer = new PerformanceAnalyzer(elementFinder);
        PerformanceAnalyzer.ResetMonitoring();

        _fixture.RunOnUIThread(() =>
        {
            var panel = new StackPanel();
            panel.Children.Add(new Border());
            Application.Current.MainWindow.Content = panel;
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.UpdateLayout();
        });

        var result = JsonSerializer.SerializeToElement(analyzer.GetRenderStats(warmUp: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isWarmedUp").GetBoolean().Should().BeTrue();
        result.GetProperty("warmUpApplied").GetBoolean().Should().BeTrue();
        result.GetProperty("sampleCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("visualCount").GetInt32().Should().BeGreaterThan(0);
        result.TryGetProperty("confidence", out _).Should().BeTrue();
    }

    [Fact]
    public void GetVisualCount_WithRootElement_ShouldExecuteSuccessfully()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new PerformanceAnalyzer(elementFinder);

            var root = new StackPanel();
            root.Children.Add(new Border());
            root.Children.Add(new StackPanel());
            Application.Current.MainWindow.Content = root;

            var rootId = elementFinder.GenerateElementId(root);
            elementFinder.TryRemoveCachedElement(rootId).Should().BeTrue();

            return JsonSerializer.SerializeToElement(analyzer.GetVisualCount(rootId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        result.GetProperty("totalCount").GetInt32().Should().Be(result.GetProperty("count").GetInt32());
        result.GetProperty("elementType").GetString().Should().Be("StackPanel");
    }

    [Fact]
    public void MeasureElementRenderTime_WithRootElement_ShouldExecuteSuccessfully()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new PerformanceAnalyzer(elementFinder);

            var border = new Border { Child = new StackPanel() };
            Application.Current.MainWindow.Content = border;

            var borderId = elementFinder.GenerateElementId(border);
            elementFinder.TryRemoveCachedElement(borderId).Should().BeTrue();

            return JsonSerializer.SerializeToElement(analyzer.MeasureElementRenderTime(borderId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("renderTimeMs").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        result.GetProperty("renderTime").GetDouble().Should().Be(result.GetProperty("renderTimeMs").GetDouble());
        result.GetProperty("confidence").GetString().Should().Be("low");
        result.GetProperty("elementType").GetString().Should().Be("Border");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<WeakReference> TrackTemporaryBindings(int count)
    {
        var weakReferences = new List<WeakReference>();
        for (int i = 0; i < count; i++)
        {
            var binding = new Binding($"Temp{i}");
            weakReferences.Add(new WeakReference(binding));
            PerformanceAnalyzer.TrackBinding(binding);
        }

        return weakReferences;
    }
}
