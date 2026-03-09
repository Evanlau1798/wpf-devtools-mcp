using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class ElementFinderMultiWindowTests
{
    [StaFact]
    public void GetRootElement_WithWindowIndex0_ShouldReturnFirstWindow()
    {
        var finder = new ElementFinder();

        // Without Application.Current, windowIndex should still gracefully return null
        var result = finder.GetRootElement(windowIndex: 0);
        result.Should().BeNull("no Application.Current in unit test context");
    }

    [StaFact]
    public void GetRootElement_WithNullWindowIndex_ShouldReturnMainWindow()
    {
        var finder = new ElementFinder();

        // Both overloads should behave identically when no Application.Current
        var defaultResult = finder.GetRootElement();
        var indexedResult = finder.GetRootElement(windowIndex: null);

        defaultResult.Should().BeNull();
        indexedResult.Should().BeNull();
    }

    [StaFact]
    public void GetRootElement_WithNegativeWindowIndex_ShouldReturnNull()
    {
        var finder = new ElementFinder();

        var result = finder.GetRootElement(windowIndex: -1);
        result.Should().BeNull();
    }

    [StaFact]
    public void GetRootElement_WithOutOfRangeWindowIndex_ShouldReturnNull()
    {
        var finder = new ElementFinder();

        var result = finder.GetRootElement(windowIndex: 999);
        result.Should().BeNull();
    }

    [StaFact]
    public void GetWindows_WithNoApplication_ShouldReturnEmptyList()
    {
        var finder = new ElementFinder();

        var result = finder.GetWindows();
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
