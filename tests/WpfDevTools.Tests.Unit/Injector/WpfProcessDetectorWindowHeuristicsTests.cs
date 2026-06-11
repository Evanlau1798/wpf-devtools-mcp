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

    [Fact]
    public void ShouldEnumerateWindowsForProcessInfo_WithoutMainWindowHandleOrTitle_ShouldReturnFalse()
    {
        var result = WpfProcessDetector.ShouldEnumerateWindowsForProcessInfo(
            IntPtr.Zero,
            mainWindowTitle: null);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldInspectModulesForProcessInfo_WithoutWindowOrMainWindowSignals_ShouldReturnFalse()
    {
        var result = WpfProcessDetector.ShouldInspectModulesForProcessInfo(
            window: null,
            mainWindowHandle: IntPtr.Zero,
            mainWindowTitle: null);

        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesWindowFilter_Visible_ShouldExcludeHiddenMinimizedAndCloakedWindows()
    {
        var visibleWindow = new TopLevelWindowSnapshot(1, IntPtr.Zero, "Visible", "HwndWrapper[Test;;]", true);
        var hiddenWindow = new TopLevelWindowSnapshot(2, IntPtr.Zero, "Hidden", "HwndWrapper[Test;;]", false);
        var minimizedWindow = new TopLevelWindowSnapshot(3, IntPtr.Zero, "Minimized", "HwndWrapper[Test;;]", true, IsMinimized: true);
        var cloakedWindow = new TopLevelWindowSnapshot(4, IntPtr.Zero, "Cloaked", "HwndWrapper[Test;;]", true, IsCloaked: true);

        WpfProcessDetector.MatchesWindowFilter(visibleWindow, ProcessWindowFilter.Visible).Should().BeTrue();
        WpfProcessDetector.MatchesWindowFilter(hiddenWindow, ProcessWindowFilter.Visible).Should().BeFalse();
        WpfProcessDetector.MatchesWindowFilter(minimizedWindow, ProcessWindowFilter.Visible).Should().BeFalse();
        WpfProcessDetector.MatchesWindowFilter(cloakedWindow, ProcessWindowFilter.Visible).Should().BeFalse();
    }

    [Fact]
    public void MatchesWindowFilter_Foreground_ShouldIncludeOnlyForegroundWindow()
    {
        var foregroundWindow = new TopLevelWindowSnapshot(5, IntPtr.Zero, "Foreground", "HwndWrapper[Test;;]", true, IsForeground: true);
        var backgroundWindow = new TopLevelWindowSnapshot(6, IntPtr.Zero, "Background", "HwndWrapper[Test;;]", true);

        WpfProcessDetector.MatchesWindowFilter(foregroundWindow, ProcessWindowFilter.Foreground).Should().BeTrue();
        WpfProcessDetector.MatchesWindowFilter(backgroundWindow, ProcessWindowFilter.Foreground).Should().BeFalse();
    }

    [Fact]
    public void SelectWindowTitles_WhenMainWindowTitleAndVisibleWindowDiffer_ShouldPreferMainWindowTitle()
    {
        var titles = WpfProcessDetector.SelectWindowTitles(
            mainWindowTitle: "63-Tool Edge Case Workbench",
            enumeratedWindowTitle: "Runtime Notes");

        titles.WindowTitle.Should().Be("63-Tool Edge Case Workbench");
        titles.SecondaryWindowTitle.Should().Be("Runtime Notes");
    }

    [Fact]
    public void SelectWindowTitles_WhenMainWindowTitleMissing_ShouldFallbackToEnumeratedWindowTitle()
    {
        var titles = WpfProcessDetector.SelectWindowTitles(
            mainWindowTitle: null,
            enumeratedWindowTitle: "Runtime Notes");

        titles.WindowTitle.Should().Be("Runtime Notes");
        titles.SecondaryWindowTitle.Should().BeNull();
    }
}
