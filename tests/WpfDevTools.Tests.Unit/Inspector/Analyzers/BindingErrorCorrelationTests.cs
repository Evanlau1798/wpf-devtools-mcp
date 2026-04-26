using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("BindingErrorTests")]
public sealed class BindingErrorCorrelationTests : IDisposable
{
    public BindingErrorCorrelationTests()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    [StaFact]
    public void CollectLocalBindingErrors_WithBindingPathError_ShouldIncludeDirectElementCorrelation()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var textBox = CreateInvalidBindingTextBox("MissingName");
        var elementId = finder.GenerateElementId(textBox);
        var errors = new List<BindingErrorInfo>();

        var collectMethod = typeof(BindingAnalyzer).GetMethod(
            "CollectLocalBindingErrors",
            BindingFlags.Instance | BindingFlags.NonPublic);

        collectMethod.Should().NotBeNull();
        collectMethod!.Invoke(analyzer, new[] { textBox, errors, CreateUnboundedBindingScanBudget() });

        errors.Should().ContainSingle();
        errors[0].ElementId.Should().Be(elementId);
        errors[0].PropertyName.Should().Be("Text");
        errors[0].BindingPath.Should().Be("MissingName");
        errors[0].SuggestedElementId.Should().BeNull();
    }

    [Fact]
    public void GetBindingErrors_WithTraceOnlyError_ShouldLeaveSuggestedElementIdNull()
    {
        var analyzer = new BindingAnalyzer();
        BindingErrorTraceListener.Instance.TraceEvent(
            null,
            "System.Windows.Data",
            TraceEventType.Error,
            40,
            "System.Windows.Data Error: 40 : BindingExpression path error: 'MissingName' property not found on 'object' ''TestViewModel'.");

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(clearAfterRead: true));
        var firstError = result.GetProperty("errors")[0];

        firstError.TryGetProperty("elementId", out var elementId).Should().BeTrue();
        elementId.ValueKind.Should().Be(JsonValueKind.Null);
        firstError.TryGetProperty("suggestedElementId", out var suggestedElementId).Should().BeTrue();
        suggestedElementId.ValueKind.Should().Be(JsonValueKind.Null);
        firstError.TryGetProperty("matchConfidence", out var matchConfidence).Should().BeTrue();
        matchConfidence.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [StaFact]
    public void CorrelateTraceError_WithAmbiguousCandidates_ShouldReturnSingleSuggestionWithLowConfidence()
    {
        var firstId = "TextBox_1";
        var secondId = "TextBox_2";
        var traceError = new BindingErrorInfo
        {
            Timestamp = DateTime.UtcNow,
            Message = "System.Windows.Data Error: 40 : BindingExpression path error: 'MissingName' property not found on 'object' ''TestViewModel'.",
            EventType = TraceEventType.Error.ToString(),
            SourceId = 40
        };
        var liveErrors = new[]
        {
            new BindingErrorInfo
            {
                Timestamp = DateTime.UtcNow,
                Message = "Binding error on TextBox.Text path 'MissingName' (PathError).",
                EventType = "PathError",
                Origin = BindingErrorInfo.OriginBindingExpression,
                ElementId = firstId,
                PropertyName = "Text",
                BindingPath = "MissingName"
            },
            new BindingErrorInfo
            {
                Timestamp = DateTime.UtcNow,
                Message = "Binding error on TextBox.Text path 'MissingName' (PathError).",
                EventType = "PathError",
                Origin = BindingErrorInfo.OriginBindingExpression,
                ElementId = secondId,
                PropertyName = "Text",
                BindingPath = "MissingName"
            }
        };

        var correlateMethod = typeof(BindingAnalyzer).GetMethod(
            "CorrelateTraceError",
            BindingFlags.Static | BindingFlags.NonPublic);

        correlateMethod.Should().NotBeNull();
        var correlated = (BindingErrorInfo?)correlateMethod!.Invoke(null, new object[] { traceError, liveErrors });

        correlated.Should().NotBeNull();
        correlated!.SuggestedElementId.Should().BeOneOf(firstId, secondId);
        correlated.MatchConfidence.Should().Be("low");
        correlated.BindingPath.Should().Be("MissingName");
    }

    private static TextBox CreateInvalidBindingTextBox(string path)
    {
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, new Binding(path)
        {
            Source = new { Name = "Alice" }
        });
        return textBox;
    }

    private static object CreateUnboundedBindingScanBudget()
    {
        var budgetType = typeof(BindingAnalyzer).GetNestedType(
            "BindingScanBudget",
            BindingFlags.NonPublic);

        budgetType.Should().NotBeNull();
        return Activator.CreateInstance(
            budgetType!,
            int.MaxValue,
            int.MaxValue,
            "TraversalLimit",
            "ResultLimit")!;
    }
}
