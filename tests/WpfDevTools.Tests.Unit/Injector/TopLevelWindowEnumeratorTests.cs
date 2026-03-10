using System;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public sealed class TopLevelWindowEnumeratorTests
{
    [Fact]
    public void SelectBestWindow_ShouldPreferVisibleWindowWithTitle()
    {
        var windows = new[]
        {
            new TopLevelWindowSnapshot(42, (IntPtr)1, null, "HwndWrapper[Hidden]", false),
            new TopLevelWindowSnapshot(42, (IntPtr)2, "Admin Console", "HwndWrapper[TestApp]", true),
            new TopLevelWindowSnapshot(42, (IntPtr)3, "Secondary", "NotWpf", true)
        };

        var selected = TopLevelWindowEnumerator.SelectBestWindow(windows, 42);

        selected.Should().NotBeNull();
        selected!.Handle.Should().Be((IntPtr)2);
        selected.Title.Should().Be("Admin Console");
    }

    [Fact]
    public void SelectBestWindow_ShouldFallbackToAnyMatchingProcessWindow()
    {
        var windows = new[]
        {
            new TopLevelWindowSnapshot(99, (IntPtr)1, "Other", "HwndWrapper[Other]", true),
            new TopLevelWindowSnapshot(42, (IntPtr)2, null, "HwndWrapper[TestApp]", false)
        };

        var selected = TopLevelWindowEnumerator.SelectBestWindow(windows, 42);

        selected.Should().NotBeNull();
        selected!.Handle.Should().Be((IntPtr)2);
    }
}
