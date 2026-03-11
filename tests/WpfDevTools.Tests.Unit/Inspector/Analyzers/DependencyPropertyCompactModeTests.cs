using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyCompactModeTests
{
    [StaFact]
    public void GetValueSource_WithCompactFalse_ShouldRetainDetailedFields()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Width", elementId));

        result.TryGetProperty("rawBaseValueSource", out _).Should().BeTrue();
        result.TryGetProperty("isExpression", out _).Should().BeTrue();
        result.TryGetProperty("localValue", out _).Should().BeTrue();
    }

    [StaFact]
    public void GetValueSource_WithCompactTrue_ShouldReturnMinimalDecisionFields()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Width", elementId, compact: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Width");
        result.GetProperty("baseValueSource").GetString().Should().Be("LocalValue");
        result.GetProperty("effectiveValue").GetString().Should().Be("120");
        result.TryGetProperty("rawBaseValueSource", out _).Should().BeFalse();
        result.TryGetProperty("localValue", out _).Should().BeFalse();
    }
}
