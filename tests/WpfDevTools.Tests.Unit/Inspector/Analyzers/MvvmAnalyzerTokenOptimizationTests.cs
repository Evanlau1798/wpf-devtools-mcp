using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class MvvmAnalyzerTokenOptimizationTests
{
    [StaFact]
    public void GetViewModel_WithPropertyNames_ShouldReturnRequestedSubsetOnly()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var elementId = finder.GenerateElementId(new Button
        {
            DataContext = new FilterableViewModel
            {
                Name = "Alice",
                Age = 30,
                IsValid = true
            }
        });

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetViewModel(elementId, new[] { "Name", "IsValid" }));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var properties = result.GetProperty("properties").EnumerateArray().ToList();
        properties.Should().HaveCount(2);
        properties.Select(property => property.GetProperty("name").GetString())
            .Should().Equal("Name", "IsValid");
    }

    [StaFact]
    public void GetViewModel_WithMixedKnownAndUnknownPropertyNames_ShouldReturnKnownProperties()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var elementId = finder.GenerateElementId(new Button
        {
            DataContext = new FilterableViewModel
            {
                Name = "Alice",
                Age = 30,
                IsValid = true
            }
        });

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetViewModel(elementId, new[] { "Missing", "Age" }));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var properties = result.GetProperty("properties").EnumerateArray().ToList();
        properties.Should().ContainSingle();
        properties[0].GetProperty("name").GetString().Should().Be("Age");
    }

    [StaFact]
    public void GetViewModel_WithOnlyUnknownPropertyNames_ShouldReturnStructuredPropertyNotFound()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var elementId = finder.GenerateElementId(new Button
        {
            DataContext = new FilterableViewModel
            {
                Name = "Alice",
                Age = 30,
                IsValid = true
            }
        });

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetViewModel(elementId, new[] { "Missing", "AlsoMissing" }));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("PropertyNotFound");
        result.GetProperty("hint").GetString().Should().Contain("get_viewmodel");
    }

    private sealed class FilterableViewModel
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public bool IsValid { get; set; }
    }
}
