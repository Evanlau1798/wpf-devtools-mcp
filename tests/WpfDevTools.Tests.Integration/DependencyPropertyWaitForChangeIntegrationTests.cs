using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public sealed class DependencyPropertyWaitForChangeIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public DependencyPropertyWaitForChangeIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WaitForChange_ShouldReturnChanged_WhenPropertyValueChanges()
    {
        ElementFinder? finder = null;
        DependencyPropertyAnalyzer? analyzer = null;
        Button? button = null;
        string? elementId = null;

        _fixture.RunOnUIThread(() =>
        {
            finder = new ElementFinder();
            analyzer = new DependencyPropertyAnalyzer(finder);
            button = new Button { Width = 100 };
            Application.Current.MainWindow.Content = button;
            elementId = finder.GenerateElementId(button);
        });

        var waitTask = Task.Run(() => JsonSerializer.SerializeToElement(
            analyzer!.WaitForChange("Width", elementId, timeoutMs: 1000, pollIntervalMs: 50)));

        await Task.Delay(150);
        _fixture.RunOnUIThread(() => button!.Width = 200);

        var result = await waitTask;
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("changed").GetBoolean().Should().BeTrue();
        result.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        result.GetProperty("observedChange").GetBoolean().Should().BeTrue();
        result.GetProperty("matchedExpectedValueAtStart").GetBoolean().Should().BeFalse();
        result.GetProperty("completionReason").GetString().Should().Be("ValueChanged");
        result.GetProperty("initialValue").GetString().Should().Be("100");
        result.GetProperty("currentValue").GetString().Should().Be("200");
    }

    [Fact]
    public async Task WaitForChange_ShouldWaitUntilExpectedValueIsReached()
    {
        ElementFinder? finder = null;
        DependencyPropertyAnalyzer? analyzer = null;
        Button? button = null;
        string? elementId = null;

        _fixture.RunOnUIThread(() =>
        {
            finder = new ElementFinder();
            analyzer = new DependencyPropertyAnalyzer(finder);
            button = new Button { Width = 100 };
            Application.Current.MainWindow.Content = button;
            elementId = finder.GenerateElementId(button);
        });

        var waitTask = Task.Run(() => JsonSerializer.SerializeToElement(
            analyzer!.WaitForChange(
                "Width",
                elementId,
                timeoutMs: 1200,
                pollIntervalMs: 50,
                expectedValue: JsonSerializer.SerializeToElement(300.0))));

        await Task.Delay(100);
        _fixture.RunOnUIThread(() => button!.Width = 200);
        await Task.Delay(100);
        _fixture.RunOnUIThread(() => button!.Width = 300);

        var result = await waitTask;
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("changed").GetBoolean().Should().BeTrue();
        result.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        result.GetProperty("observedChange").GetBoolean().Should().BeTrue();
        result.GetProperty("matchedExpectedValueAtStart").GetBoolean().Should().BeFalse();
        result.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        result.GetProperty("currentValue").GetString().Should().Be("300");
    }
}
