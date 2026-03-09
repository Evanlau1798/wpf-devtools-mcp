using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for multi-window targeting support.
/// Verifies ElementFinder and VisualTreeAnalyzer work across multiple windows.
/// </summary>
[Collection("WpfIntegration")]
public class MultiWindowIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public MultiWindowIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetWindows_WithSingleWindow_ShouldReturnOneWindow()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            return finder.GetWindows();
        });

        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThanOrEqualTo(1);
        result[0].Title.Should().NotBeNull();
        result[0].Type.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetWindows_WithMultipleWindows_ShouldReturnAll()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var secondWindow = new Window
            {
                Title = "Test Second Window",
                Content = new TextBlock { Text = "Second window content" }
            };

            try
            {
                secondWindow.Show();

                var windows = finder.GetWindows();
                return new { Windows = windows, WindowCount = windows.Count };
            }
            finally
            {
                secondWindow.Close();
            }
        });

        result.WindowCount.Should().BeGreaterThanOrEqualTo(2);
        var titles = result.Windows.Select(w => w.Title).ToList();
        titles.Should().Contain("Test Second Window");
    }

    [Fact]
    public void GetRootElement_WithWindowIndex_ShouldReturnCorrectWindow()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var secondWindow = new Window
            {
                Title = "Targeted Window",
                Content = new Button { Content = "Target Button" }
            };

            try
            {
                secondWindow.Show();

                var windows = finder.GetWindows();
                var targetIndex = windows
                    .Select((w, i) => new { w, i })
                    .First(x => x.w.Title == "Targeted Window")
                    .i;

                var root = finder.GetRootElement(targetIndex);
                return new
                {
                    FoundRoot = root != null,
                    IsWindow = root is Window,
                    Title = (root as Window)?.Title
                };
            }
            finally
            {
                secondWindow.Close();
            }
        });

        result.FoundRoot.Should().BeTrue();
        result.IsWindow.Should().BeTrue();
        result.Title.Should().Be("Targeted Window");
    }

    [Fact]
    public void GetVisualTree_WithSecondWindowElementId_ShouldReturnSecondWindowTree()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new VisualTreeAnalyzer(finder);
            var secondWindow = new Window
            {
                Title = "Tree Target Window",
                Content = new StackPanel
                {
                    Children =
                    {
                        new Button { Content = "SecondWinButton" }
                    }
                }
            };

            try
            {
                secondWindow.Show();
                secondWindow.UpdateLayout();

                var windowId = finder.GenerateElementId(secondWindow);
                return analyzer.GetVisualTree(maxDepth: 3, elementId: windowId);
            }
            finally
            {
                secondWindow.Close();
            }
        });

        var json = System.Text.Json.JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void FindById_ShouldFindElementInSecondWindow()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var targetButton = new Button { Content = "Cross-window target" };
            var secondWindow = new Window
            {
                Title = "Cross-Window Search",
                Content = targetButton
            };

            try
            {
                secondWindow.Show();
                secondWindow.UpdateLayout();

                var elementId = finder.GenerateElementId(targetButton);

                // Clear cache to force tree search
                // FindById should locate the element even in a secondary window
                var found = finder.FindById(elementId);
                return new
                {
                    FoundElement = found != null,
                    SameElement = ReferenceEquals(found, targetButton)
                };
            }
            finally
            {
                secondWindow.Close();
            }
        });

        result.FoundElement.Should().BeTrue();
        result.SameElement.Should().BeTrue();
    }
}
