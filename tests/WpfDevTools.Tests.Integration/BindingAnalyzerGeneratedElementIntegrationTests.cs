using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class BindingAnalyzerGeneratedElementIntegrationTests : IDisposable
{
    private readonly WpfApplicationFixture _fixture;

    public BindingAnalyzerGeneratedElementIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void GetBindings_OnTemplateGeneratedTextBlock_ShouldReportGeneratedBinding()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new BindingAnalyzer(finder);
            var host = CreateGeneratedBindingHost(new BrokenDetailContextPayload());

            Application.Current.MainWindow.Content = host;
            Application.Current.MainWindow.Show();
            host.ApplyTemplate();
            host.Measure(new Size(800, 600));
            host.Arrange(new Rect(0, 0, 800, 600));
            host.UpdateLayout();
            Application.Current.MainWindow.UpdateLayout();

            var generatedTextBlock = FindGeneratedTextBlock(host);
            generatedTextBlock.Should().NotBeNull();

            var elementId = finder.GenerateElementId(generatedTextBlock!);
            return JsonSerializer.SerializeToElement(analyzer.GetBindings(elementId));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("bindings").GetArrayLength().Should().BeGreaterThan(0);
        result.GetProperty("bindings")[0].GetProperty("path").GetString().Should().Be("MissingNestedProperty");
    }

    [Fact]
    public void GetBindingErrors_OnTemplateGeneratedTextBlock_ShouldReportUnresolvedLiveBinding()
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var finder = new ElementFinder();
            var analyzer = new BindingAnalyzer(finder);
            var host = CreateGeneratedBindingHost(new BrokenDetailContextPayload());

            Application.Current.MainWindow.Content = host;
            Application.Current.MainWindow.Show();
            host.ApplyTemplate();
            host.Measure(new Size(800, 600));
            host.Arrange(new Rect(0, 0, 800, 600));
            host.UpdateLayout();
            Application.Current.MainWindow.UpdateLayout();

            var generatedTextBlock = FindGeneratedTextBlock(host);
            generatedTextBlock.Should().NotBeNull();
            generatedTextBlock!.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();

            BindingErrorTraceListener.Instance.GetErrors().Should().BeEmpty();

            return JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: false));
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("bindingPath").GetString())
            .Should().Contain("MissingNestedProperty");
    }

    private static ContentControl CreateGeneratedBindingHost(object dataContext)
    {
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(FrameworkElement.NameProperty, "GeneratedProbe");
        textFactory.SetBinding(
            TextBlock.TextProperty,
            new Binding("MissingNestedProperty"));

        var template = new DataTemplate
        {
            VisualTree = textFactory
        };

        return new ContentControl
        {
            Content = dataContext,
            ContentTemplate = template
        };
    }

    private static TextBlock? FindGeneratedTextBlock(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock textBlock && textBlock.Name == "GeneratedProbe")
            {
                return textBlock;
            }

            var nestedMatch = FindGeneratedTextBlock(child);
            if (nestedMatch != null)
            {
                return nestedMatch;
            }
        }

        return null;
    }

    private sealed class BrokenDetailContextPayload
    {
        public object Nested => new { DetailText = "unused" };
    }
}
