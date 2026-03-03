using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class XamlSerializerTests
{
    [StaFact]
    public void SerializeToXaml_WithButton_ShouldReturnXaml()
    {
        // Arrange
        var serializer = new XamlSerializer();
        var button = new Button { Content = "Test", Width = 100 };

        // Act
        var result = serializer.SerializeToXaml(button);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("<Button");
        result.Should().Contain("Width=\"100\"");
    }

    [StaFact]
    public void SerializeToXaml_WithComplexTree_ShouldSerializeHierarchy()
    {
        // Arrange
        var serializer = new XamlSerializer();
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new Button { Content = "Button1" });
        stackPanel.Children.Add(new TextBox { Text = "Text1" });

        // Act
        var result = serializer.SerializeToXaml(stackPanel);

        // Assert
        result.Should().Contain("<StackPanel");
        result.Should().Contain("<Button");
        result.Should().Contain("<TextBox");
    }
}
