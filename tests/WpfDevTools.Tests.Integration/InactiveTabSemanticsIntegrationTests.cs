using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class InactiveTabSemanticsIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public InactiveTabSemanticsIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetFormSummary_OnInactiveTabContent_ShouldUseInactiveTabBlocker()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new FormSummaryAnalyzer(finder);
            var tabControl = CreateTabbedWindowContent(out _, out _);

            Application.Current.MainWindow.Content = tabControl;
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.UpdateLayout();

            return JsonSerializer.SerializeToElement(analyzer.GetFormSummary());
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("commands")
            .EnumerateArray()
            .SelectMany(command => command.GetProperty("blockers").EnumerateArray())
            .Select(blocker => blocker.GetString())
            .Should()
            .Contain("ElementInInactiveTab");
    }

    [Fact]
    public void SimulateKeyboard_OnInactiveTabElement_ShouldReturnTabActivationHint()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new InteractionAnalyzer(finder);
            var tabControl = CreateTabbedWindowContent(out _, out var inactiveInput);

            Application.Current.MainWindow.Content = tabControl;
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.UpdateLayout();

            var elementId = finder.GenerateElementId(inactiveInput);
            return JsonSerializer.SerializeToElement(analyzer.SimulateKeyboard(elementId, "Tab", "KeyDown"));
        });

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("ElementNotLoaded");
        result.GetProperty("hint").GetString().Should().Contain("TabItem");
    }

    [Fact]
    public void GetLayoutInfo_OnInactiveTabContent_ShouldExplainNotRenderedReason()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new LayoutAnalyzer(finder);
            var tabControl = CreateTabbedWindowContent(out _, out var inactiveInput);

            Application.Current.MainWindow.Content = tabControl;
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.UpdateLayout();

            var elementId = finder.GenerateElementId(inactiveInput);
            return JsonSerializer.SerializeToElement(analyzer.GetLayoutInfo(elementId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("layoutState").GetString().Should().Be("NotRendered");
        result.GetProperty("notRenderedReason").GetString().Should().Be("ElementInInactiveTab");
    }

    private static TabControl CreateTabbedWindowContent(out Button activeButton, out TextBox inactiveInput)
    {
        activeButton = new Button
        {
            Name = "ActiveButton",
            Content = "Visible Action"
        };
        inactiveInput = new TextBox
        {
            Name = "InactiveInput",
            Text = "Hidden Tab"
        };

        var activeTab = new TabItem
        {
            Header = "Active",
            Content = new StackPanel
            {
                Children =
                {
                    activeButton
                }
            }
        };
        var inactiveTab = new TabItem
        {
            Header = "Inactive",
            Content = new StackPanel
            {
                Children =
                {
                    inactiveInput,
                    new Button
                    {
                        Name = "InactiveSubmit",
                        Content = "Submit"
                    }
                }
            }
        };

        return new TabControl
        {
            Items = { activeTab, inactiveTab },
            SelectedIndex = 0
        };
    }
}
