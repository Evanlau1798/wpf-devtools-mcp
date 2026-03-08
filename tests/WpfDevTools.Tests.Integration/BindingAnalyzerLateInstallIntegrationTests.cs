using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class BindingAnalyzerLateInstallIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;

    public BindingAnalyzerLateInstallIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void GetBindingErrors_WhenListenerInstalledAfterBindingsWereEvaluated_ShouldStillReportLiveErrors()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var stackPanel = new StackPanel
            {
                DataContext = new { Name = "Alice" }
            };

            var invalidPathTextBox = new TextBox();
            invalidPathTextBox.SetBinding(TextBox.TextProperty, new Binding("NonExistentProperty"));
            stackPanel.Children.Add(invalidPathTextBox);

            var nullContextPanel = new StackPanel
            {
                DataContext = null
            };

            var nullContextTextBox = new TextBox();
            nullContextTextBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
            nullContextPanel.Children.Add(nullContextTextBox);
            stackPanel.Children.Add(nullContextPanel);

            Application.Current.MainWindow.Content = stackPanel;
            Application.Current.MainWindow.UpdateLayout();

            invalidPathTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            nullContextTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

            BindingErrorTraceListener.Instance.GetErrors().Should().BeEmpty();

            var analyzer = new BindingAnalyzer(new ElementFinder());
            return analyzer.GetBindingErrors(clearAfterRead: false);
        });

        result.Should().NotBeNull();
        dynamic bindingErrors = result;
        ((bool)bindingErrors.success).Should().BeTrue();
        ((int)bindingErrors.errorCount).Should().BeGreaterThan(0);
    }
}
