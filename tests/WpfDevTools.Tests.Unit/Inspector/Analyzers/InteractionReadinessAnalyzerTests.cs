using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InteractionReadinessAnalyzerTests
{
    [StaFact]
    public void GetInteractionReadiness_WhenCommandCannotExecute_ShouldReportBlocker()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var viewModel = new TestViewModel();
        var button = new Button
        {
            Name = "SaveButton",
            Command = viewModel.SaveCommand,
            DataContext = viewModel
        };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetInteractionReadiness(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isReady").GetBoolean().Should().BeFalse();
        result.GetProperty("blockers").EnumerateArray()
            .Select(item => item.GetProperty("reason").GetString())
            .Should().Contain("CommandCannotExecute");
    }

    [StaFact]
    public void GetInteractionReadiness_WhenElementIsDisabled_ShouldReportNotReady()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button
        {
            Name = "PrimaryDisabledButton",
            IsEnabled = false
        };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetInteractionReadiness(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isReady").GetBoolean().Should().BeFalse();
        result.GetProperty("elementState").GetProperty("isEnabled").GetBoolean().Should().BeFalse();
    }
}
