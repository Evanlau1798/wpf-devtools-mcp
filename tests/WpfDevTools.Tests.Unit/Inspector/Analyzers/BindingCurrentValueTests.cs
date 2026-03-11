using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingCurrentValueTests
{
    [StaFact]
    public void GetBindings_ShouldIncludeCurrentValueForActiveBinding()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, new Binding("Name")
        {
            Source = new { Name = "Alice" },
            Mode = BindingMode.OneWay
        });
        var elementId = finder.GenerateElementId(textBox);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId));
        var binding = result.GetProperty("bindings")[0];

        binding.GetProperty("currentValue").GetString().Should().Be("Alice");
    }

    [StaFact]
    public void GetBindings_WithErrorStatusFilter_ShouldStillIncludeCurrentValue()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var panel = new StackPanel();
        var errorBox = new TextBox();
        errorBox.SetBinding(TextBox.TextProperty, new Binding("Missing")
        {
            Source = new { Name = "Alice" },
            Mode = BindingMode.OneWay
        });
        panel.Children.Add(errorBox);
        var elementId = finder.GenerateElementId(panel);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId, recursive: true, statusFilter: "Error"));
        var binding = result.GetProperty("bindings")[0];

        binding.TryGetProperty("currentValue", out _).Should().BeTrue();
    }
}
