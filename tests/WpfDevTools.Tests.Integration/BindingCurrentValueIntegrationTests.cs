using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class BindingCurrentValueIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public BindingCurrentValueIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetBindings_ShouldReturnConfigurationAndCurrentValueInOneCall()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new BindingAnalyzer(finder);
            var textBox = new TextBox();
            textBox.SetBinding(TextBox.TextProperty, new Binding("Name")
            {
                Source = new { Name = "Golden Value" },
                Mode = BindingMode.OneWay
            });

            Application.Current.MainWindow.Content = textBox;
            var elementId = finder.GenerateElementId(textBox);

            return JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId));
        });

        var binding = result.GetProperty("bindings")[0];
        binding.GetProperty("propertyName").GetString().Should().Be("Text");
        binding.GetProperty("path").GetString().Should().Be("Name");
        binding.GetProperty("currentValue").GetString().Should().Be("Golden Value");
    }

    [Fact]
    public void GetBindings_WithMultiBinding_ShouldReturnPathsConverterAndCurrentValue()
    {
        var result = _fixture.RunOnUIThread(() =>
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

            Application.Current.MainWindow.Content = textBlock;
            var elementId = finder.GenerateElementId(textBlock);

            return JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId));
        });

        var binding = result.GetProperty("bindings")[0];
        binding.GetProperty("bindingType").GetString().Should().Be("MultiBinding");
        binding.GetProperty("converter").GetString().Should().Be(nameof(TestConcatMultiConverter));
        binding.GetProperty("currentValue").GetString().Should().Be("Ada Lovelace");
        binding.GetProperty("bindingPaths").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("FirstName", "LastName");
    }

    private sealed class TestConcatMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => string.Join(" ", values.OfType<string>());

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
