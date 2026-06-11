using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class BindingErrorCorrelationIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;
    private readonly SourceLevels _originalBindingTraceLevel;

    public BindingErrorCorrelationIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        _originalBindingTraceLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        BindingErrorTraceListener.ResetInstance();
        PresentationTraceSources.DataBindingSource.Switch.Level = _originalBindingTraceLevel;
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
        PresentationTraceSources.DataBindingSource.Switch.Level = _originalBindingTraceLevel;
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
