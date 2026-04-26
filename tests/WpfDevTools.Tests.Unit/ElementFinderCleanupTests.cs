using Xunit;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit;

public class ElementFinderCleanupTests
{
    [StaFact]
    public void CleanupDeadReferences_ShouldPreserveLiveTrackedElements()
    {
        using var finder = new ElementFinder();
        var buttons = new List<Button>();
        var elementIds = new List<string>();

        for (int i = 0; i < 100; i++)
        {
            var button = new Button { Content = $"Button {i}" };
            buttons.Add(button);
            elementIds.Add(finder.GenerateElementId(button));
        }

        finder.CleanupDeadReferences();

        finder.GetTrackedElements().Should().HaveCount(100);
        for (int i = 0; i < elementIds.Count; i++)
        {
            finder.FindById(elementIds[i]).Should().BeSameAs(buttons[i]);
        }
    }

    [StaFact]
    public void TryRemoveCachedElement_ShouldEvictTrackedElementById()
    {
        using var finder = new ElementFinder();
        var button = new Button { Content = "Remove me" };
        var elementId = finder.GenerateElementId(button);

        finder.GetTrackedElements().Should().ContainSingle().Which.Should().BeSameAs(button);

        finder.TryRemoveCachedElement(elementId).Should().BeTrue();

        finder.GetTrackedElements().Should().BeEmpty();
        finder.TryRemoveCachedElement(elementId).Should().BeFalse();
    }

    [StaFact]
    public void ElementFinder_ShouldImplementIDisposable()
    {
        var finder = new ElementFinder();

        Assert.IsAssignableFrom<IDisposable>(finder);

        finder.Dispose();
    }
}
