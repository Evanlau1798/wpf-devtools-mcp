using System.Text.Json;
using System.Diagnostics;
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

    [Fact]
    public void GetBindingErrors_WhenTraceQueueHasOlderErrors_ShouldStillIncludeNewerLiveOnlyErrors()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var stackPanel = new StackPanel
            {
                DataContext = new { Name = "Alice" }
            };

            var liveOnlyTextBox = new TextBox();
            liveOnlyTextBox.SetBinding(TextBox.TextProperty, new Binding("MissingDetailName"));
            stackPanel.Children.Add(liveOnlyTextBox);

            Application.Current.MainWindow.Content = stackPanel;
            Application.Current.MainWindow.UpdateLayout();
            liveOnlyTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

            BindingErrorTraceListener.Instance.TraceEvent(
                null,
                "System.Windows.Data",
                TraceEventType.Error,
                40,
                "System.Windows.Data Error: 40 : BindingExpression path error: 'LegacyName' property not found on 'object' ''TestViewModel'.");

            var cutoff = DateTime.UtcNow.AddMilliseconds(50);
            Thread.Sleep(75);

            var analyzer = new BindingAnalyzer(new ElementFinder());
            return JsonSerializer.SerializeToElement(
                analyzer.GetBindingErrors(
                    sinceTimestamp: cutoff.ToString("O"),
                    clearAfterRead: false));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("bindingPath").GetString())
            .Should().Contain("MissingDetailName");
    }
}
