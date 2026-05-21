using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class ValidationTraversalBudgetTests
{
    [StaFact]
    public void GetValidationErrors_WithLargeZeroErrorTree_ShouldReturnTraversalBudgetMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var root = new StackPanel();

        for (var index = 0; index < 600; index++)
        {
            root.Children.Add(new TextBox());
        }

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetValidationErrors(finder.GenerateElementId(root)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
        result.GetProperty("traversalTruncated").GetBoolean().Should().BeTrue();
        result.GetProperty("traversalNodeCount").GetInt32().Should().BeLessOrEqualTo(512);
        result.GetProperty("maxTraversalNodes").GetInt32().Should().Be(512);
    }

    [StaFact]
    public void GetValidationErrors_WhenErrorLimitIsHit_ShouldSeparateResultAndTraversalTruncation()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var root = new StackPanel();
        var rule = new ExceptionValidationRule();

        for (var index = 0; index < 250; index++)
        {
            var textBox = new TextBox();
            textBox.SetBinding(TextBox.TextProperty, new Binding("Text")
            {
                Source = new { Text = "" },
                Mode = BindingMode.OneWay
            });
            var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            Validation.MarkInvalid(expression!, new ValidationError(rule, expression!)
            {
                ErrorContent = $"Error {index}"
            });
            root.Children.Add(textBox);
        }

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetValidationErrors(finder.GenerateElementId(root)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(200);
        result.GetProperty("validationErrorsTruncated").GetBoolean().Should().BeTrue();
        result.GetProperty("maxValidationErrors").GetInt32().Should().Be(200);
        result.GetProperty("traversalTruncated").GetBoolean().Should().BeFalse();
    }
}
