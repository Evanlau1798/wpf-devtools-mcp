using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingStatusFilterTests
{
    [StaFact]
    public void GetBindings_WithActiveStatusFilter_ShouldReturnOnlyActiveBindings()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var panel = new StackPanel();
        var activeBox = new TextBox();
        var errorBox = new TextBox();
        activeBox.SetBinding(TextBox.TextProperty, new Binding("Name") { Source = new { Name = "Alice" }, Mode = BindingMode.OneWay });
        errorBox.SetBinding(TextBox.TextProperty, new Binding("Missing") { Source = new { Name = "Alice" }, Mode = BindingMode.OneWay });
        panel.Children.Add(activeBox);
        panel.Children.Add(errorBox);
        var elementId = finder.GenerateElementId(panel);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId, recursive: true, statusFilter: "Active"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var bindings = result.GetProperty("bindings").EnumerateArray().ToList();
        bindings.Should().NotBeEmpty();
        bindings.Select(binding => binding.GetProperty("status").GetString()).Should().OnlyContain(status => status == "Active");
    }

    [StaFact]
    public void GetBindings_WithErrorStatusFilter_ShouldReturnOnlyErrorBindings()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var panel = new StackPanel();
        var activeBox = new TextBox();
        var errorBox = new TextBox();
        activeBox.SetBinding(TextBox.TextProperty, new Binding("Name") { Source = new { Name = "Alice" }, Mode = BindingMode.OneWay });
        errorBox.SetBinding(TextBox.TextProperty, new Binding("Missing") { Source = new { Name = "Alice" }, Mode = BindingMode.OneWay });
        panel.Children.Add(activeBox);
        panel.Children.Add(errorBox);
        var elementId = finder.GenerateElementId(panel);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId, recursive: true, statusFilter: "Error"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var bindings = result.GetProperty("bindings").EnumerateArray().ToList();
        bindings.Should().NotBeEmpty();
        bindings.Select(binding => binding.GetProperty("status").GetString())
            .Should().OnlyContain(status => status == "PathError" || status == "UpdateTargetError" || status == "UpdateSourceError");
    }

    [StaFact]
    public void GetBindings_WithInvalidStatusFilter_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var panel = new StackPanel();
        var elementId = finder.GenerateElementId(panel);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId, statusFilter: "Broken"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
    }
}
