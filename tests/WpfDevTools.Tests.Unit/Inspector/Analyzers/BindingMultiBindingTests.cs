using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingMultiBindingTests
{
    [StaFact]
    public void GetBindings_WithMultiBinding_ShouldReturnChildPathsConverterAndCurrentValue()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var textBlock = new TextBlock();
        var source = new { FirstName = "Ada", LastName = "Lovelace" };
        textBlock.SetBinding(TextBlock.TextProperty, new MultiBinding
        {
            Converter = new TestConcatMultiConverter(),
            Bindings =
            {
                new Binding("FirstName") { Source = source },
                new Binding("LastName") { Source = source }
            }
        });
        var elementId = finder.GenerateElementId(textBlock);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId));
        var binding = result.GetProperty("bindings")[0];

        binding.GetProperty("bindingType").GetString().Should().Be("MultiBinding");
        binding.GetProperty("converter").GetString().Should().Be(nameof(TestConcatMultiConverter));
        binding.GetProperty("currentValue").GetString().Should().Be("Ada Lovelace");
        binding.GetProperty("bindingPaths").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("FirstName", "LastName");
    }

    [StaFact]
    public void GetBindingValueChain_WithMultiBinding_ShouldReturnChildPathsConverterAndFinalValue()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var textBlock = new TextBlock();
        var source = new { FirstName = "Ada", LastName = "Lovelace" };
        textBlock.SetBinding(TextBlock.TextProperty, new MultiBinding
        {
            Converter = new TestConcatMultiConverter(),
            Bindings =
            {
                new Binding("FirstName") { Source = source },
                new Binding("LastName") { Source = source }
            }
        });
        var elementId = finder.GenerateElementId(textBlock);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingValueChain(elementId, "Text"));

        result.GetProperty("success").GetBoolean().Should().BeTrue(result.GetRawText());
        result.GetProperty("hasBinding").GetBoolean().Should().BeTrue(result.GetRawText());
        result.GetProperty("chain").EnumerateArray()
            .Any(step =>
                step.TryGetProperty("bindingType", out var bindingType)
                && bindingType.GetString() == "MultiBinding")
            .Should().BeTrue(result.GetRawText());
        result.GetProperty("chain").EnumerateArray()
            .SelectMany(step =>
                step.TryGetProperty("bindingPaths", out var bindingPaths)
                    ? bindingPaths.EnumerateArray().Select(item => item.GetString())
                    : Array.Empty<string?>())
            .Should().Contain(new[] { "FirstName", "LastName" });
        result.GetProperty("chain").EnumerateArray()
            .Any(step =>
                step.TryGetProperty("converter", out var converter)
                && converter.GetString() == nameof(TestConcatMultiConverter))
            .Should().BeTrue(result.GetRawText());
        result.GetProperty("chain").EnumerateArray()
            .Any(step =>
                step.TryGetProperty("step", out var stepName)
                && stepName.GetString() == "FinalValue"
                && step.TryGetProperty("value", out var value)
                && value.GetString() == "Ada Lovelace")
            .Should().BeTrue(result.GetRawText());
    }

    private sealed class TestConcatMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => string.Join(" ", values.OfType<string>());

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
