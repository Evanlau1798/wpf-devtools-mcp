using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class WpfProcessDetectorWindowHeuristicsTests
{
    [Fact]
    public void BuildBestWindowIndex_ShouldPickHighestPriorityWindowPerProcess()
    {
        var windows = new[]
        {
            new TopLevelWindowSnapshot(10, IntPtr.Zero, null, "WinFormsApp", IsVisible: false),
            new TopLevelWindowSnapshot(10, IntPtr.Zero, "Hidden", "HwndWrapper[Test;;]", IsVisible: false),
            new TopLevelWindowSnapshot(10, IntPtr.Zero, "Visible", "HwndWrapper[Test;;]", IsVisible: true),
            new TopLevelWindowSnapshot(11, IntPtr.Zero, "Secondary", "OtherWindow", IsVisible: true)
        };

        var index = WpfProcessDetector.BuildBestWindowIndex(windows);

        index.Should().HaveCount(2);
        index[10].Title.Should().Be("Visible");
        index[11].Title.Should().Be("Secondary");
    }

    [Theory]
    [InlineData("HwndWrapper[TestApp;;]", true)]
    [InlineData("WindowsForms10.Window.8.app.0.2bf8098_r6_ad1", false)]
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData("", true)]
    public void ShouldInspectModules_ShouldSkipKnownNonWpfWindowClasses(string? className, bool expected)
    {
        var window = new TopLevelWindowSnapshot(42, IntPtr.Zero, "Window", className, IsVisible: true);

        var result = WpfProcessDetector.ShouldInspectModules(window);

        result.Should().Be(expected);
    }
}
