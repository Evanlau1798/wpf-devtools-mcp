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
}
