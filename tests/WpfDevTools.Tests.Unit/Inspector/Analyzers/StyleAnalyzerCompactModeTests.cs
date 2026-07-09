using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class StyleAnalyzerCompactModeTests
{
    [StaFact]
    public void GetAppliedStyles_WithCompactTrue_ShouldReturnStyleSummaries()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button
        {
            Style = new Style(typeof(Button))
            {
                Setters =
                {
                    new Setter(Control.WidthProperty, 120.0),
                    new Setter(Control.HeightProperty, 32.0)
                }
            }
        };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetAppliedStyles(elementId, compact: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasStyle").GetBoolean().Should().BeTrue();
        result.GetProperty("styleCount").GetInt32().Should().Be(1);
        var style = result.GetProperty("styles")[0];
        style.GetProperty("setterCount").GetInt32().Should().Be(2);
        style.TryGetProperty("setters", out _).Should().BeFalse();
    }

    [StaFact]
    public void GetAppliedStyles_WithCompactFalse_ShouldRetainDetailedSetters()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var button = new Button
        {
            Style = new Style(typeof(Button))
            {
                Setters =
                {
                    new Setter(Control.WidthProperty, 120.0)
                }
            }
        };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetAppliedStyles(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("styles")[0].TryGetProperty("setters", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetAppliedStyles_WithImplicitStyleResource_ShouldReportImplicitSourceAndFullTargetType()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var window = new Window();

        try
        {
            var button = new Button();
            var implicitStyle = new Style(typeof(Button))
            {
                Setters =
                {
                    new Setter(Control.HeightProperty, 32.0)
                }
            };
            window.Resources.Add(typeof(Button), implicitStyle);
            window.Content = button;
            window.Show();
            button.ApplyTemplate();

            var elementId = finder.GenerateElementId(button);

            var result = JsonSerializer.SerializeToElement(analyzer.GetAppliedStyles(elementId, compact: true));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("hasStyle").GetBoolean().Should().BeTrue();
            result.GetProperty("styleCount").GetInt32().Should().Be(1);
            var appliedStyle = result.GetProperty("styles")[0];
            appliedStyle.GetProperty("styleType").GetString().Should().Be("Implicit");
            appliedStyle.GetProperty("baseValueSource").GetString().Should().Be("ImplicitStyleReference");
            appliedStyle.GetProperty("targetTypeFullName").GetString().Should().Be(typeof(Button).FullName);
        }
        finally
        {
            window.Close();
        }
    }
}
