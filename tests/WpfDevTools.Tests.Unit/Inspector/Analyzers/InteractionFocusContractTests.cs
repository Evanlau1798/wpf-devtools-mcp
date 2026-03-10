using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InteractionFocusContractTests
{
    [StaFact]
    public void GetFocusState_ShouldReturnFocusedElementMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window();
        var first = new TextBox();
        var second = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        window.Content = panel;

        var expectedId = finder.GenerateElementId(first);
        var windowId = finder.GenerateElementId(window);
        FocusManager.SetFocusedElement(window, first);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFocusState(windowId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("focusKind").GetString().Should().Be("Logical");
        result.GetProperty("focusedElementId").GetString().Should().Be(expectedId);
        result.GetProperty("windowTitle").GetString().Should().BeEmpty();
    }

    [StaFact]
    public void FocusElement_ShouldMoveLogicalFocusToTarget()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var window = new Window();
        var first = new TextBox();
        var second = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        window.Content = panel;

        FocusManager.SetFocusedElement(window, first);
        var secondId = finder.GenerateElementId(second);
        var windowId = finder.GenerateElementId(window);

        var focusResult = JsonSerializer.SerializeToElement(analyzer.FocusElement(secondId));
        var stateResult = JsonSerializer.SerializeToElement(analyzer.GetFocusState(windowId));

        focusResult.GetProperty("success").GetBoolean().Should().BeTrue();
        focusResult.GetProperty("focused").GetBoolean().Should().BeTrue();
        stateResult.GetProperty("focusedElementId").GetString().Should().Be(secondId);
    }
}
