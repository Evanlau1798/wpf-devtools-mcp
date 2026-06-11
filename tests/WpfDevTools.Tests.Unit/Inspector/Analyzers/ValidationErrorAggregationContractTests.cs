using System.Text.Json;
using FluentAssertions;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

/// <summary>
/// Contract regression tests for MvvmAnalyzer.GetValidationErrors ensuring
/// aggregation behavior and response schema remain stable across refactors.
/// </summary>
public sealed class ValidationErrorAggregationContractTests
{
    [StaFact]
    public void GetValidationErrors_OnParent_ShouldAggregateChildErrors()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var stackPanel = new StackPanel();
        var textBox1 = new TextBox { Name = "FirstInput" };
        var textBox2 = new TextBox { Name = "SecondInput" };
        stackPanel.Children.Add(textBox1);
        stackPanel.Children.Add(textBox2);

        InjectValidationError(textBox1, "First field is required");
        InjectValidationError(textBox2, "Second field is required");

        var parentId = finder.GenerateElementId(stackPanel);

        // Act
        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetValidationErrors(parentId)));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterOrEqualTo(2,
            "Parent element must aggregate validation errors from descendant elements");

        var errors = result.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterOrEqualTo(2);
    }

    [StaFact]
    public void GetValidationErrors_ResponseErrors_ShouldIncludeElementTypeMetadata()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var stackPanel = new StackPanel();
        var textBox = new TextBox { Name = "ValidatedField" };
        stackPanel.Children.Add(textBox);

        InjectValidationError(textBox, "Field validation failed");

        var parentId = finder.GenerateElementId(stackPanel);

        // Act
        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetValidationErrors(parentId)));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var errors = result.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterOrEqualTo(1);

        var firstError = errors[0];
        firstError.TryGetProperty("elementType", out var elementType).Should().BeTrue(
            "Each error entry must include an elementType field");
        elementType.GetString().Should().Be("TextBox",
            "elementType must match the actual WPF element type name");

        firstError.TryGetProperty("elementName", out var elementName).Should().BeTrue(
            "Each error entry must include an elementName field");
        elementName.GetString().Should().Be("ValidatedField");
    }

    [StaFact]
    public void GetValidationErrors_OnSingleElement_ShouldReturnOnlyItsErrors()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var textBox = new TextBox { Name = "StandaloneField" };

        InjectValidationError(textBox, "Standalone validation error");

        var elementId = finder.GenerateElementId(textBox);

        // Act
        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetValidationErrors(elementId)));

        // Assert
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(1,
            "Single element without children should report exactly its own errors");

        var firstError = result.GetProperty("errors")[0];
        firstError.GetProperty("errorContent").GetString().Should().Be("Standalone validation error");
        firstError.GetProperty("elementType").GetString().Should().Be("TextBox");
    }

    /// <summary>
    /// Sets up a OneWay binding and injects a validation error on the given TextBox.
    /// </summary>
    private static void InjectValidationError(TextBox textBox, string errorMessage)
    {
        var binding = new System.Windows.Data.Binding("Text")
        {
            Source = new { Text = "" },
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        textBox.SetBinding(TextBox.TextProperty, binding);

        var expr = System.Windows.Data.BindingOperations.GetBindingExpression(
            textBox, TextBox.TextProperty);
        var rule = new ExceptionValidationRule();
        Validation.MarkInvalid(
            expr!,
            new ValidationError(rule, expr!) { ErrorContent = errorMessage });
    }
}
