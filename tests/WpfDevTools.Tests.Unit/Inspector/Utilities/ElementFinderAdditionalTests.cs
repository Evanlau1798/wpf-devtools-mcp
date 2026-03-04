using Xunit;
using FluentAssertions;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class ElementFinderAdditionalTests
{
    [StaFact]
    public void GenerateElementId_ShouldReturnTypeName_WithNumber()
    {
        // Arrange
        var finder = new ElementFinder();
        var button = new Button();

        // Act
        var id = finder.GenerateElementId(button);

        // Assert
        id.Should().StartWith("Button_");
        id.Should().MatchRegex(@"^Button_\d+$");
    }

    [StaFact]
    public void GenerateElementId_SameElement_ShouldReturnSameId()
    {
        // Arrange
        var finder = new ElementFinder();
        var button = new Button();

        // Act
        var id1 = finder.GenerateElementId(button);
        var id2 = finder.GenerateElementId(button);

        // Assert
        id1.Should().Be(id2);
    }

    [StaFact]
    public void GenerateElementId_DifferentElements_ShouldReturnDifferentIds()
    {
        // Arrange
        var finder = new ElementFinder();
        var button1 = new Button();
        var button2 = new Button();

        // Act
        var id1 = finder.GenerateElementId(button1);
        var id2 = finder.GenerateElementId(button2);

        // Assert
        id1.Should().NotBe(id2);
    }

    [StaFact]
    public void GenerateElementId_WithTextBlock_ShouldIncludeTypeName()
    {
        // Arrange
        var finder = new ElementFinder();
        var textBlock = new System.Windows.Controls.TextBlock();

        // Act
        var id = finder.GenerateElementId(textBlock);

        // Assert
        id.Should().StartWith("TextBlock_");
    }

    [Fact]
    public void FindById_WithNullId_ShouldReturnNull_WhenNoApp()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var result = finder.FindById(null);

        // Assert
        // No Application.Current in unit test, so GetRootElement() returns null
        result.Should().BeNull();
    }

    [Fact]
    public void FindById_WithEmptyId_ShouldReturnNull_WhenNoApp()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var result = finder.FindById("");

        // Assert
        result.Should().BeNull();
    }

    [StaFact]
    public void FindById_WithGeneratedId_ShouldFindElement()
    {
        // Arrange
        var finder = new ElementFinder();
        var button = new Button();
        var id = finder.GenerateElementId(button);

        // Act
        var found = finder.FindById(id);

        // Assert
        found.Should().BeSameAs(button);
    }

    [Fact]
    public void FindById_WithUnknownId_ShouldReturnNull_WhenNoApp()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var result = finder.FindById("unknown_id_12345");

        // Assert
        // Cache miss and no root element => null
        result.Should().BeNull();
    }

    [Fact]
    public void GetRootElement_WithNoApplication_ShouldReturnNull()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var root = finder.GetRootElement();

        // Assert
        root.Should().BeNull();
    }

    [StaFact]
    public void GenerateElementId_MultipleCalls_ShouldUseCache_NotIncrementId()
    {
        // Arrange
        var finder = new ElementFinder();
        var button = new Button();

        // Act
        var id1 = finder.GenerateElementId(button);
        var id2 = finder.GenerateElementId(button);
        var id3 = finder.GenerateElementId(button);

        // Assert - all calls return the same cached id
        id1.Should().Be(id2);
        id2.Should().Be(id3);
    }

    [StaFact]
    public void FindById_AfterGenerateId_PopulatesWeakReferenceCache()
    {
        // Arrange
        var finder = new ElementFinder();
        var checkbox = new CheckBox();
        var id = finder.GenerateElementId(checkbox);

        // Act - FindById should retrieve from WeakReference cache
        var result1 = finder.FindById(id);
        var result2 = finder.FindById(id);

        // Assert
        result1.Should().BeSameAs(checkbox);
        result2.Should().BeSameAs(checkbox);
    }
}
