using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class DependencyPropertyAnalyzerTests
{
    [StaFact]
    public void GetValueSource_WithValidProperty_ShouldReturnSource()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetValueSource("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("valueSource");
            resultDict["success"].Should().Be(true);
            resultDict["valueSource"].Should().NotBeNull();
        }
    }

    [StaFact]
    public void SetValue_WithValidProperty_ShouldSetValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.SetValue("Width", 200.0, elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
        button.Width.Should().Be(200.0);
    }

    [StaFact]
    public void ClearValue_WithLocalValue_ShouldClearValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ClearValue("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
        button.Width.Should().Be(double.NaN); // Default value
    }

    [StaFact]
    public void GetMetadata_WithValidProperty_ShouldReturnMetadata()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetMetadata("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("metadata");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void WatchChanges_WithValidProperty_ShouldStartWatching()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.WatchChanges("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
    }
}
