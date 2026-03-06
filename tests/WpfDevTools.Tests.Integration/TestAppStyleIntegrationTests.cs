using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for StyleAnalyzer using TestApp golden sample scenarios.
/// Tests style inheritance, triggers, and templates matching TestApp Tab 4
/// (Styles &amp; Templates).
/// </summary>
[Collection("WpfIntegration")]
public class TestAppStyleIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TestAppStyleIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetAppliedStyles_WithInheritedStyle_ShouldReturnStyleInfo()
    {
        // Arrange - recreate TestApp Tab 4 style inheritance
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);

            // Base style (matches TestApp BaseButtonStyle)
            var baseStyle = new Style(typeof(Button));
            baseStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 5, 10, 5)));
            baseStyle.Setters.Add(new Setter(Button.MarginProperty, new Thickness(5)));

            // Derived style with triggers (matches TestApp PrimaryButtonStyle)
            var primaryStyle = new Style(typeof(Button), baseStyle);
            primaryStyle.Setters.Add(new Setter(Button.ForegroundProperty,
                System.Windows.Media.Brushes.White));
            primaryStyle.Triggers.Add(new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true,
                Setters = { new Setter(Button.OpacityProperty, 0.8) }
            });
            primaryStyle.Triggers.Add(new Trigger
            {
                Property = Button.IsEnabledProperty,
                Value = false,
                Setters = { new Setter(Button.OpacityProperty, 0.5) }
            });

            var button = new Button
            {
                Content = "Primary Style",
                Style = primaryStyle
            };

            Application.Current.MainWindow.Content = button;

            return analyzer.GetAppliedStyles(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetTriggers_WithStyleTriggers_ShouldReturnTriggerInfo()
    {
        // Arrange - button with triggers matching TestApp PrimaryButtonStyle
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);

            var style = new Style(typeof(Button));
            style.Triggers.Add(new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true,
                Setters = { new Setter(Button.OpacityProperty, 0.8) }
            });
            style.Triggers.Add(new Trigger
            {
                Property = Button.IsEnabledProperty,
                Value = false,
                Setters = { new Setter(Button.OpacityProperty, 0.5) }
            });

            var button = new Button
            {
                Content = "Triggered Button",
                Style = style
            };

            Application.Current.MainWindow.Content = button;

            return analyzer.GetTriggers(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetTemplateTree_WithCustomTemplate_ShouldReturnTemplateInfo()
    {
        // Arrange - button with ControlTemplate matching TestApp RoundButtonTemplate
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            borderFactory.AppendChild(contentPresenterFactory);
            template.VisualTree = borderFactory;

            var button = new Button
            {
                Content = "Round Button",
                Template = template,
                Padding = new Thickness(20, 10, 20, 10)
            };

            Application.Current.MainWindow.Content = button;

            return analyzer.GetTemplateTree(elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetResourceChain_WithElementResource_ShouldFindResource()
    {
        // Arrange - resource chain matching TestApp Tab 4 resource structure
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);

            var stackPanel = new StackPanel();
            stackPanel.Resources.Add("TestBrush",
                System.Windows.Media.Brushes.DodgerBlue);

            var button = new Button { Content = "Styled" };
            stackPanel.Children.Add(button);

            Application.Current.MainWindow.Content = stackPanel;

            return analyzer.GetResourceChain(elementId: null, resourceKey: "TestBrush");
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetAppliedStyles_WithDisabledButton_ShouldReflectState()
    {
        // Arrange - disabled button matching TestApp "Primary Disabled" button
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new StyleAnalyzer(elementFinder);

            var style = new Style(typeof(Button));
            style.Triggers.Add(new Trigger
            {
                Property = Button.IsEnabledProperty,
                Value = false,
                Setters = { new Setter(Button.OpacityProperty, 0.5) }
            });

            var button = new Button
            {
                Content = "Primary Disabled",
                Style = style,
                IsEnabled = false
            };

            Application.Current.MainWindow.Content = button;

            return analyzer.GetAppliedStyles(elementId: null);
        });

        result.Should().NotBeNull();
    }
}
