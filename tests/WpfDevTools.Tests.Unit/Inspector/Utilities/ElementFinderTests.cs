using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class ElementFinderTests
{
    [Fact]
    public void GetRootElement_WhenNoApplication_ShouldReturnNull()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var root = finder.GetRootElement();

        // Assert
        // In unit test environment, Application.Current is null
        root.Should().BeNull();
    }

    [StaFact]
    public void GenerateElementId_WithSameElement_ShouldReturnSameId()
    {
        // Arrange
        var finder = new ElementFinder();
        var element = new Button();

        // Act
        var id1 = finder.GenerateElementId(element);
        var id2 = finder.GenerateElementId(element);

        // Assert
        id1.Should().Be(id2);
        id1.Should().StartWith("Button_");
    }

    [Fact]
    public void FindById_WithNullId_ShouldReturnRoot()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var element = finder.FindById(null);

        // Assert
        // In unit test environment, both should return null
        element.Should().Be(finder.GetRootElement());
    }
}
