using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class LayoutVisibleDescendantBoundsTests
{
    [StaFact]
    public void GetClippingInfo_ScrollContent_ShouldNotMakeVisibleParentAppearClipped()
    {
        using var finder = new ElementFinder();
        var content = new StackPanel { Width = 180 };
        var target = new Border { Height = 300, Width = 180 };
        content.Children.Add(target);
        var scroll = new ScrollViewer { Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var parent = new Border { Child = scroll };
        var window = new Window { Width = 200, Height = 120, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, Content = parent, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            var analyzer = new LayoutAnalyzer(finder);
            var parentResult = JsonSerializer.SerializeToElement(analyzer.GetClippingInfo(finder.GenerateElementId(parent)));
            var childResult = JsonSerializer.SerializeToElement(analyzer.GetClippingInfo(finder.GenerateElementId(target)));

            parentResult.GetProperty("isClipped").GetBoolean().Should().BeFalse(parentResult.GetRawText());
            childResult.GetProperty("isClipped").GetBoolean().Should().BeTrue();
            childResult.GetProperty("nearestScrollContainer").GetProperty("canBringTargetIntoView")
                .GetBoolean().Should().BeTrue();
        }
        finally { window.Close(); }
    }
}
