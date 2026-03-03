using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Media;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class SerializationHelperTests
{
    [Theory]
    [InlineData(42, 42)]
    [InlineData("test", "test")]
    [InlineData(true, true)]
    [InlineData(null, null)]
    public void SerializePropertyValue_WithSimpleTypes_ShouldReturnValue(object? input, object? expected)
    {
        // Act
        var result = SerializationHelper.SerializePropertyValue(input);

        // Assert
        result.Should().Be(expected);
    }

    [StaFact]
    public void SerializePropertyValue_WithSolidColorBrush_ShouldReturnColorString()
    {
        // Arrange
        var brush = new SolidColorBrush(Colors.Red);

        // Act
        var result = SerializationHelper.SerializePropertyValue(brush);

        // Assert
        result.Should().Be("SolidColorBrush: #FFFF0000");
    }
}
