using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class EventAnalyzerTests
{
    [StaFact]
    public void TraceRoutedEvents_WithValidElement_ShouldStartTracing()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.TraceRoutedEvents(elementId, "Click", 1000);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void FireRoutedEvent_WithValidEvent_ShouldRaiseEvent()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var eventFired = false;
        button.Click += (s, e) => eventFired = true;

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "Click", null);

        // Assert
        eventFired.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [StaFact]
    public void GetEventTrace_ShouldReturnTraceData()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);

        // Act
        var result = analyzer.GetEventTrace();

        // Assert
        result.Should().NotBeNull();
    }
}
