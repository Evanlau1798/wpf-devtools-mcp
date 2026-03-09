using System.Collections;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingValueChainDiagnosticsTests
{
    [StaFact]
    public void GetBindingValueChain_WithNullLocalDataContext_ShouldDescribeAncestorResolution()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var ancestorViewModel = new { Name = "Ancestor" };
        var parent = new StackPanel { DataContext = ancestorViewModel };
        var textBox = new TextBox { DataContext = null };
        textBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
        parent.Children.Add(textBox);
        var elementId = finder.GenerateElementId(textBox);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetBindingValueChain(elementId, "Text")));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasBinding").GetBoolean().Should().BeTrue();

        var chain = result.GetProperty("chain").EnumerateArray().ToArray();
        chain.Any(step => step.GetProperty("step").GetString() == "LocalDataContext").Should().BeTrue();
        chain.Any(step => step.GetProperty("step").GetString() == "InheritedDataContext").Should().BeTrue();
    }
}
