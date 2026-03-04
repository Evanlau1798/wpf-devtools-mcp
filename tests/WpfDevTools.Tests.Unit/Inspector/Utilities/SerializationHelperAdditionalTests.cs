using Xunit;
using FluentAssertions;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class SerializationHelperAdditionalTests
{
    [StaFact]
    public void SerializePropertyValue_WithSolidColorBrush_ShouldReturnFormattedString()
    {
        // Arrange
        var brush = new SolidColorBrush(Colors.Red);

        // Act
        var result = SerializationHelper.SerializePropertyValue(brush);

        // Assert
        result.Should().BeOfType<string>();
        var str = (string)result!;
        str.Should().StartWith("SolidColorBrush: #");
        str.Should().Be("SolidColorBrush: #FFFF0000");
    }

    [StaFact]
    public void SerializePropertyValue_WithLinearGradientBrush_ShouldReturnTypeName()
    {
        // Arrange
        var brush = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);

        // Act
        var result = SerializationHelper.SerializePropertyValue(brush);

        // Assert
        result.Should().BeOfType<string>();
        ((string)result!).Should().Be("LinearGradientBrush");
    }

    [StaFact]
    public void SerializePropertyValue_WithSolidColorBrush_Blue_ShouldReturnCorrectHex()
    {
        // Arrange
        var brush = new SolidColorBrush(Colors.Blue);

        // Act
        var result = SerializationHelper.SerializePropertyValue(brush);

        // Assert
        result.Should().Be("SolidColorBrush: #FF0000FF");
    }

    [StaFact]
    public void SerializePropertyValue_WithSolidColorBrush_Transparent_ShouldReturnCorrectHex()
    {
        // Arrange
        var brush = new SolidColorBrush(Colors.Transparent);

        // Act
        var result = SerializationHelper.SerializePropertyValue(brush);

        // Assert
        result.Should().BeOfType<string>();
        var str = (string)result!;
        str.Should().StartWith("SolidColorBrush: #");
        // Transparent is #00FFFFFF
        str.Should().Be("SolidColorBrush: #00FFFFFF");
    }

    [StaFact]
    public void SerializePropertyValue_WithRadialGradientBrush_ShouldReturnTypeName()
    {
        // Arrange
        var brush = new RadialGradientBrush(Colors.White, Colors.Black);

        // Act
        var result = SerializationHelper.SerializePropertyValue(brush);

        // Assert
        result.Should().BeOfType<string>();
        ((string)result!).Should().Be("RadialGradientBrush");
    }

    [Fact]
    public void SerializePropertyValue_WithEnum_ShouldReturnToString()
    {
        // Arrange
        var visibility = System.Windows.Visibility.Collapsed;

        // Act
        var result = SerializationHelper.SerializePropertyValue(visibility);

        // Assert
        result.Should().BeOfType<string>();
        ((string)result!).Should().Be("Collapsed");
    }

    [Fact]
    public void SerializePropertyValue_WithDouble_ShouldReturnValue()
    {
        // Act
        var result = SerializationHelper.SerializePropertyValue(3.14);

        // Assert
        result.Should().Be(3.14);
    }

    [Fact]
    public void SerializePropertyValue_WithDecimal_ShouldReturnValue()
    {
        // Act
        var result = SerializationHelper.SerializePropertyValue(9.99m);

        // Assert
        result.Should().Be(9.99m);
    }
}
