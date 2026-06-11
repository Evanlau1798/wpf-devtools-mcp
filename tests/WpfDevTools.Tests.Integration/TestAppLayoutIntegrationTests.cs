using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfDevTools.Tests.TestApp;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for LayoutAnalyzer using TestApp golden sample scenarios.
/// Tests clipping, transforms, and layout info matching TestApp Tab 6
/// (Layout &amp; Transforms).
/// </summary>
[Collection("WpfAndBootstrapIntegration")]
public sealed class TestAppLayoutIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private Window? _previousMainWindow;
    private MainWindow? _activeTestAppWindow;

    public TestAppLayoutIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        _fixture.RunOnUIThread(() =>
        {
            if (_activeTestAppWindow == null)
            {
                return;
            }

            _activeTestAppWindow.Close();
            _activeTestAppWindow = null;

            if (Application.Current != null)
            {
                Application.Current.MainWindow = _previousMainWindow;
            }

            _previousMainWindow = null;
        });
    }

    [Fact]
    public void GetClippingInfo_WithClipToBounds_ShouldDetectClipping()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectLayoutTab(context);

            var elementId = elementFinder.GenerateElementId(context.ClippingTextSample);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetClippingInfo(elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetClippingInfo(elementId));

            return new
            {
                cachedResult,
                lookupResult
            };
        });

        var cachedResult = result.cachedResult;
        var lookupResult = result.lookupResult;

        cachedResult.GetRawText().Should().Be(lookupResult.GetRawText());
        cachedResult.GetProperty("success").GetBoolean().Should().BeTrue();
        cachedResult.GetProperty("isClipped").GetBoolean().Should().BeTrue(cachedResult.GetRawText());
        cachedResult.GetProperty("clipToBounds").GetBoolean().Should().BeFalse();
        cachedResult.GetProperty("hasClip").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void GetClippingInfo_WithWrappedTextInClipContainer_ShouldDetectChildAndContainerClipping()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectLayoutTab(context);

            var textId = elementFinder.GenerateElementId(context.ClippingTextSample);
            var borderId = elementFinder.GenerateElementId(context.ClippingBorderSample);
            var cachedChild = analyzer.GetClippingInfo(textId);
            var cachedContainer = analyzer.GetClippingInfo(borderId);
            EvictElementCacheEntry(elementFinder, textId);
            EvictElementCacheEntry(elementFinder, borderId);

            return new
            {
                CachedChild = cachedChild,
                CachedContainer = cachedContainer,
                LookupChild = analyzer.GetClippingInfo(textId),
                LookupContainer = analyzer.GetClippingInfo(borderId)
            };
        });

        var diagnostics = JsonSerializer.Serialize(result);
        var cachedChild = JsonSerializer.SerializeToElement(result.CachedChild);
        var lookupChild = JsonSerializer.SerializeToElement(result.LookupChild);
        var child = cachedChild;
        cachedChild.GetRawText().Should().Be(lookupChild.GetRawText());
        child.GetProperty("success").GetBoolean().Should().BeTrue();
        child.GetProperty("isClipped").GetBoolean().Should().BeTrue(diagnostics);

        var cachedContainer = JsonSerializer.SerializeToElement(result.CachedContainer);
        var lookupContainer = JsonSerializer.SerializeToElement(result.LookupContainer);
        var container = cachedContainer;
        cachedContainer.GetRawText().Should().Be(lookupContainer.GetRawText());
        container.GetProperty("success").GetBoolean().Should().BeTrue();
        container.GetProperty("clipToBounds").GetBoolean().Should().BeTrue();
        container.GetProperty("isClipped").GetBoolean().Should().BeTrue(diagnostics);
        container.GetProperty("hasClip").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void GetLayoutInfo_WithRenderTransform_ShouldReturnLayoutInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectLayoutTab(context);

            var renderTransform = context.RotatedButtonSample.RenderTransform.Should().BeOfType<RotateTransform>().Subject;
            renderTransform.Angle.Should().Be(15);

            var elementId = elementFinder.GenerateElementId(context.RotatedButtonSample);
            var cachedResult = JsonSerializer.SerializeToElement(analyzer.GetLayoutInfo(elementId));
            EvictElementCacheEntry(elementFinder, elementId);
            var lookupResult = JsonSerializer.SerializeToElement(analyzer.GetLayoutInfo(elementId));

            return new
            {
                TransformAngle = renderTransform.Angle,
                cachedResult,
                lookupResult
            };
        });

        var cachedResult = result.cachedResult;
        var lookupResult = result.lookupResult;

        result.TransformAngle.Should().Be(15);
        cachedResult.GetRawText().Should().Be(lookupResult.GetRawText());
        cachedResult.GetProperty("success").GetBoolean().Should().BeTrue();
        cachedResult.GetProperty("layoutState").GetString().Should().Be("Rendered");
        cachedResult.GetProperty("actualWidth").GetDouble().Should().BeGreaterThan(0);
        cachedResult.GetProperty("actualHeight").GetDouble().Should().BeGreaterThan(0);
        cachedResult.GetProperty("margin").GetProperty("left").GetDouble().Should().Be(5);
        cachedResult.GetProperty("notRenderedReason").ValueKind.Should().Be(JsonValueKind.Null);
        cachedResult.GetProperty("positionInWindow").GetProperty("x").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        cachedResult.GetProperty("positionInWindow").GetProperty("y").GetDouble().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetLayoutInfo_WithLayoutTransform_ShouldReturnLayoutInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectLayoutTab(context);

            var layoutTransform = context.LayoutRotatedButtonSample.LayoutTransform.Should().BeOfType<RotateTransform>().Subject;
            layoutTransform.Angle.Should().Be(45);

            var layoutRotatedId = elementFinder.GenerateElementId(context.LayoutRotatedButtonSample);
            var renderRotatedId = elementFinder.GenerateElementId(context.RotatedButtonSample);
            var cachedLayoutRotated = analyzer.GetLayoutInfo(layoutRotatedId);
            var cachedRenderRotated = analyzer.GetLayoutInfo(renderRotatedId);
            EvictElementCacheEntry(elementFinder, layoutRotatedId);
            EvictElementCacheEntry(elementFinder, renderRotatedId);

            return JsonSerializer.SerializeToElement(new
            {
                cachedLayoutRotated,
                lookupLayoutRotated = analyzer.GetLayoutInfo(layoutRotatedId),
                cachedRenderRotated,
                lookupRenderRotated = analyzer.GetLayoutInfo(renderRotatedId)
            });
        });

        var cachedLayoutRotated = result.GetProperty("cachedLayoutRotated");
        var lookupLayoutRotated = result.GetProperty("lookupLayoutRotated");
        var cachedRenderRotated = result.GetProperty("cachedRenderRotated");
        var lookupRenderRotated = result.GetProperty("lookupRenderRotated");
        var layoutRotated = cachedLayoutRotated;
        var renderRotated = cachedRenderRotated;

        cachedLayoutRotated.GetRawText().Should().Be(lookupLayoutRotated.GetRawText());
        cachedRenderRotated.GetRawText().Should().Be(lookupRenderRotated.GetRawText());
        layoutRotated.GetProperty("success").GetBoolean().Should().BeTrue();
        layoutRotated.GetProperty("layoutState").GetString().Should().Be("Rendered");
        layoutRotated.GetProperty("actualWidth").GetDouble().Should().BeGreaterThan(0);
        layoutRotated.GetProperty("actualHeight").GetDouble().Should().BeGreaterThan(0);
        layoutRotated.GetProperty("desiredHeight").GetDouble().Should().BeGreaterThan(
            renderRotated.GetProperty("desiredHeight").GetDouble());
    }

    [Fact]
    public void GetLayoutInfo_WithScaleTransform_ShouldReturnLayoutInfo()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            using var elementFinder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(elementFinder);
            var context = CreateRealTestAppWindow();
            SelectLayoutTab(context);

            var scaleTransform = context.ScaledButtonSample.RenderTransform.Should().BeOfType<ScaleTransform>().Subject;
            scaleTransform.ScaleX.Should().Be(1.2);
            scaleTransform.ScaleY.Should().Be(1.2);

            var elementId = elementFinder.GenerateElementId(context.ScaledButtonSample);
            var cachedLayout = JsonSerializer.SerializeToElement(analyzer.GetLayoutInfo(elementId));
            EvictElementCacheEntry(elementFinder, elementId);

            return new
            {
                scaleTransform.ScaleX,
                scaleTransform.ScaleY,
                cachedLayout,
                lookupLayout = JsonSerializer.SerializeToElement(analyzer.GetLayoutInfo(elementId))
            };
        });

        var cachedLayout = result.cachedLayout;
        var lookupLayout = result.lookupLayout;
        var layout = cachedLayout;

        result.ScaleX.Should().Be(1.2);
        result.ScaleY.Should().Be(1.2);
        cachedLayout.GetRawText().Should().Be(lookupLayout.GetRawText());
        layout.GetProperty("success").GetBoolean().Should().BeTrue();
        layout.GetProperty("layoutState").GetString().Should().Be("Rendered");
        layout.GetProperty("actualWidth").GetDouble().Should().BeGreaterThan(0);
        layout.GetProperty("actualHeight").GetDouble().Should().BeGreaterThan(0);
        layout.GetProperty("margin").GetProperty("left").GetDouble().Should().Be(5);
        layout.GetProperty("notRenderedReason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static void EvictElementCacheEntry(ElementFinder elementFinder, string elementId)
    {
        elementFinder.TryRemoveCachedElement(elementId).Should().BeTrue();
    }

    private static void SelectLayoutTab(TestAppLayoutWindowContext context)
    {
        context.MainTabControl.SelectedItem = context.LayoutTransformsTab;
        context.Window.UpdateLayout();
    }

    private TestAppLayoutWindowContext CreateRealTestAppWindow()
    {
        var application = Application.Current;
        application.Should().NotBeNull();

        _previousMainWindow ??= application!.MainWindow;

        var window = new MainWindow();
        _activeTestAppWindow = window;
        application.MainWindow = window;
        window.Show();
        window.UpdateLayout();

        var mainTabControl = window.FindName("MainTabControl") as TabControl;
        var layoutTransformsTab = window.FindName("LayoutTransformsTab") as TabItem;
        var clippingBorderSample = window.FindName("ClippingBorderSample") as Border;
        var clippingTextSample = window.FindName("ClippingTextSample") as TextBlock;
        var rotatedButtonSample = window.FindName("RotatedButtonSample") as Button;
        var scaledButtonSample = window.FindName("ScaledButtonSample") as Button;
        var layoutRotatedButtonSample = window.FindName("LayoutRotatedButtonSample") as Button;

        mainTabControl.Should().NotBeNull();
        layoutTransformsTab.Should().NotBeNull();
        clippingBorderSample.Should().NotBeNull();
        clippingTextSample.Should().NotBeNull();
        rotatedButtonSample.Should().NotBeNull();
        scaledButtonSample.Should().NotBeNull();
        layoutRotatedButtonSample.Should().NotBeNull();

        return new TestAppLayoutWindowContext(
            window,
            mainTabControl!,
            layoutTransformsTab!,
            clippingBorderSample!,
            clippingTextSample!,
            rotatedButtonSample!,
            scaledButtonSample!,
            layoutRotatedButtonSample!);
    }

    private sealed record TestAppLayoutWindowContext(
        MainWindow Window,
        TabControl MainTabControl,
        TabItem LayoutTransformsTab,
        Border ClippingBorderSample,
        TextBlock ClippingTextSample,
        Button RotatedButtonSample,
        Button ScaledButtonSample,
        Button LayoutRotatedButtonSample);
}
