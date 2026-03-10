using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyValueSourceNormalizationTests
{
    [StaFact]
    public void GetValueSource_ShouldExposeNormalizedAndRawBaseValueSource()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Width", elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("baseValueSource").GetString().Should().Be("LocalValue");
        result.GetProperty("rawBaseValueSource").GetString().Should().Be("Local");
    }
}
