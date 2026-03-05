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

    [Fact]
    public void FindById_WithEmptyId_ShouldReturnRoot()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var element = finder.FindById("");

        // Assert
        element.Should().Be(finder.GetRootElement());
    }

    [Fact]
    public void FindById_WithInvalidIdFormat_ShouldReturnNull()
    {
        // Arrange
        var finder = new ElementFinder();
        var invalidId = new string('x', 257); // >256 chars

        // Act
        var element = finder.FindById(invalidId);

        // Assert
        element.Should().BeNull();
    }

    [StaFact]
    public void FindById_WithCachedElement_ShouldReturnFromCache()
    {
        // Arrange
        var finder = new ElementFinder();
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var found = finder.FindById(elementId);

        // Assert
        found.Should().BeSameAs(button);
    }

    [StaFact]
    public void FindById_WithNonExistentId_ShouldReturnNull()
    {
        // Arrange
        var finder = new ElementFinder();

        // Act
        var element = finder.FindById("Button_99999");

        // Assert
        element.Should().BeNull();
    }

    [StaFact]
    public void GenerateElementId_WithDifferentElements_ShouldReturnDifferentIds()
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
    public void GenerateElementId_ShouldIncludeTypeName()
    {
        // Arrange
        var finder = new ElementFinder();
        var textBox = new TextBox();

        // Act
        var id = finder.GenerateElementId(textBox);

        // Assert
        id.Should().StartWith("TextBox_");
    }

    [StaFact]
    public void FindById_WithVisualTreeSearch_ShouldFindNestedElement()
    {
        // Arrange
        var finder = new ElementFinder();
        var parent = new StackPanel();
        var child = new Button();
        parent.Children.Add(child);

        var childId = finder.GenerateElementId(child);

        // Act - search from parent
        var found = finder.FindById(childId, parent);

        // Assert
        found.Should().BeSameAs(child);
    }

    [StaFact]
    public void CleanupDeadReferences_RemovesEntriesForCollectedElements()
    {
        // Arrange
        using var finder = new ElementFinder();
        var id = CreateAndTrackElement(finder);

        // Act - Force GC to collect the unreferenced element
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        finder.CleanupDeadReferences();

        // Assert - Element should no longer be findable by ID
        var found = finder.FindById(id);
        found.Should().BeNull();
    }

    private static string CreateAndTrackElement(ElementFinder finder)
    {
        var element = new TextBlock();
        return finder.GenerateElementId(element);
    }
}
