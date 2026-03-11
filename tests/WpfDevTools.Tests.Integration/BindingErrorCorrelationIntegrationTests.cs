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
public sealed class BindingErrorCorrelationIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public BindingErrorCorrelationIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void GetBindingErrors_ShouldProvideActionableElementCorrelation()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new BindingAnalyzer(finder);
            var panel = new StackPanel();

            var errorTextBox = new TextBox();
            errorTextBox.SetBinding(TextBox.TextProperty, new Binding("MissingName")
            {
                Source = new { Name = "Alice" }
            });

            panel.Children.Add(errorTextBox);
            Application.Current.MainWindow.Content = panel;
            errorTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

            var errors = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: true));
            var actionableId = errors.GetProperty("errors")[0].GetProperty("elementId").GetString();
            actionableId.Should().NotBeNullOrWhiteSpace();

            return JsonSerializer.SerializeToElement(analyzer.GetBindings(actionableId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("bindings").GetArrayLength().Should().BeGreaterThan(0);
    }
}
