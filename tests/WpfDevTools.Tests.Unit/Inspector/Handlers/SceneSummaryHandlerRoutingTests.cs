using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class SceneSummaryHandlerRoutingTests
{
    [Fact]
    public void SceneSummaryHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new SceneSummaryHandlers(
            new UiSummaryAnalyzer(new ElementFinder()),
            new FormSummaryAnalyzer(new ElementFinder()));

        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(2);
        methods.Should().Contain("get_ui_summary");
        methods.Should().Contain("get_form_summary");
    }

    [Fact]
    public async Task SceneSummaryHandlers_HandleAsync_GetUiSummary_ShouldReturnResult()
    {
        var handler = new SceneSummaryHandlers(
            new UiSummaryAnalyzer(new ElementFinder()),
            new FormSummaryAnalyzer(new ElementFinder()));

        var result = await handler.HandleAsync("get_ui_summary", null, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SceneSummaryHandlers_HandleAsync_GetFormSummary_ShouldReturnResult()
    {
        var handler = new SceneSummaryHandlers(
            new UiSummaryAnalyzer(new ElementFinder()),
            new FormSummaryAnalyzer(new ElementFinder()));

        var parameters = JsonDocument.Parse("{\"elementId\":\"missing\"}").RootElement;
        var result = await handler.HandleAsync("get_form_summary", parameters, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [StaFact]
    public async Task SceneSummaryHandlers_HandleAsync_GetUiSummary_WithSummaryOnly_ShouldOmitNodes()
    {
        var finder = new ElementFinder();
        var root = new StackPanel
        {
            Name = "SummaryRoot",
            Children =
            {
                new TextBox { Name = "NameBox", Text = "Ada" }
            }
        };
        var elementId = finder.GenerateElementId(root);
        var handler = new SceneSummaryHandlers(
            new UiSummaryAnalyzer(finder),
            new FormSummaryAnalyzer(new ElementFinder()));
        var parameters = JsonSerializer.SerializeToElement(new { elementId, summaryOnly = true });

        var result = JsonSerializer.SerializeToElement(
            await handler.HandleAsync("get_ui_summary", parameters, CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summaryText").GetString().Should().Contain("NameBox");
        result.TryGetProperty("nodes", out _).Should().BeFalse();
    }
}
