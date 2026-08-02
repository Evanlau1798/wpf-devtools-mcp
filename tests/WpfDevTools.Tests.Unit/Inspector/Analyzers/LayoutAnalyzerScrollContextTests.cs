using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class LayoutAnalyzerScrollContextTests
{
    [StaFact]
    public void GetClippingInfo_ForPartiallyVisibleChild_ShouldDescribeNearestScrollContainer()
    {
        using var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var target = new Border { Width = 100, Height = 40, Name = "TrailingCard" };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border { Width = 180, Height = 40 });
        content.Children.Add(target);
        var scrollViewer = new ScrollViewer
        {
            Name = "CardRail",
            Width = 200,
            Height = 60,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
        var window = new Window
        {
            Width = 300,
            Height = 160,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = scrollViewer
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            var result = analyzer.GetClippingInfo(finder.GenerateElementId(target));
            var doc = JsonSerializer.SerializeToElement(result);
            var scrollDoc = JsonSerializer.SerializeToElement(
                analyzer.GetClippingInfo(finder.GenerateElementId(scrollViewer)));

            doc.GetProperty("geometricClippingSeverity").GetString().Should().Be("partial");
            var scroll = doc.GetProperty("nearestScrollContainer");
            scroll.GetProperty("elementName").GetString().Should().Be("CardRail");
            scroll.GetProperty("horizontalOverflow").GetBoolean().Should().BeTrue();
            scroll.GetProperty("verticalOverflow").GetBoolean().Should().BeFalse();
            scroll.GetProperty("horizontalScrollBarVisibility").GetString().Should().Be("Hidden");
            scroll.GetProperty("viewportWidth").GetDouble().Should().BeLessThan(
                scroll.GetProperty("extentWidth").GetDouble());
            scroll.GetProperty("isTargetClippedByViewport").GetBoolean().Should().BeTrue(
                "target payload: {0}; scroll payload: {1}",
                doc.ToString(),
                scrollDoc.ToString());
            scroll.GetProperty("canBringTargetIntoView").GetBoolean().Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetClippingInfo_ForScrollViewerTarget_ShouldNotReportItAsItsOwnContainer()
    {
        using var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var target = new ScrollViewer
        {
            Width = 100,
            Height = 40,
            Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 1, 1)),
            Content = new Border { Width = 300, Height = 40 }
        };
        var window = new Window
        {
            Width = 200,
            Height = 120,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = target
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            var result = analyzer.GetClippingInfo(finder.GenerateElementId(target));
            var doc = JsonSerializer.SerializeToElement(result);

            doc.GetProperty("nearestScrollContainer").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetClippingInfo_ForNestedScrollViewers_ShouldReportCausalOuterContainer()
    {
        using var finder = new ElementFinder();
        var analyzer = new LayoutAnalyzer(finder);
        var target = new Border { Width = 60, Height = 40, Name = "NestedTarget" };
        var inner = new ScrollViewer
        {
            Name = "InnerRail",
            Width = 100,
            Height = 60,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = target
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border { Width = 180, Height = 60 });
        content.Children.Add(inner);
        var outer = new ScrollViewer
        {
            Name = "OuterRail",
            Width = 200,
            Height = 70,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
        var window = new Window
        {
            Width = 400,
            Height = 160,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = outer
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            var result = analyzer.GetClippingInfo(finder.GenerateElementId(target));
            var doc = JsonSerializer.SerializeToElement(result);
            var scroll = doc.GetProperty("nearestScrollContainer");

            scroll.GetProperty("elementName").GetString().Should().Be("OuterRail");
            scroll.GetProperty("isTargetClippedByViewport").GetBoolean().Should().BeTrue();
            scroll.GetProperty("canBringTargetIntoView").GetBoolean().Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }
}
