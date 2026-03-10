using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class StyleAnalyzerContractTests
{
    [StaFact]
    public void GetAppliedStyles_WhenElementUsesLocalResourceReference_ShouldExplainNoStyleResult()
    {
        var finder = new ElementFinder();
        var analyzer = new StyleAnalyzer(finder);
        var window = EnsureHostWindow();
        try
        {
            var button = new Button { Content = "Modern" };

            window.Resources["AccentBrush"] = Brushes.SeaGreen;
            button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            window.Content = button;
            window.Show();
            window.UpdateLayout();

            var elementId = finder.GenerateElementId(button);
            var result = JsonSerializer.SerializeToElement(analyzer.GetAppliedStyles(elementId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("hasStyle").GetBoolean().Should().BeFalse();
            result.GetProperty("localResourceReferenceCount").GetInt32().Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window EnsureHostWindow()
    {
        return new Window();
    }
}
