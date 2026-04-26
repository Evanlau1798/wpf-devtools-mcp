using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("BindingErrorTests")]
public sealed class DiagnosticNormalizationTests
{
    [Fact]
    public void GetBindingErrors_ShouldExposeNormalizedDiagnosticFields()
    {
        BindingErrorTraceListener.ResetInstance();
        var analyzer = new BindingAnalyzer();
        BindingErrorTraceListener.Instance.TraceEvent(
            null,
            "System.Windows.Data",
            TraceEventType.Error,
            40,
            "BindingExpression path error: 'Missing' not found");

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors());
        var firstError = result.GetProperty("errors")[0];

        firstError.GetProperty("diagnosticKind").GetString().Should().Be("BindingError");
        firstError.GetProperty("sourceKind").GetString().Should().Be("BindingTrace");
        firstError.GetProperty("severity").GetString().Should().Be("Error");
    }

    [Fact]
    public void GetBindingErrors_FromTraceListener_ShouldHaveBindingTraceSourceKind()
    {
        BindingErrorTraceListener.ResetInstance();
        var analyzer = new BindingAnalyzer();
        BindingErrorTraceListener.Instance.TraceEvent(
            null,
            "System.Windows.Data",
            TraceEventType.Error,
            40,
            "BindingExpression path error: 'Missing' not found");

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors());
        var firstError = result.GetProperty("errors")[0];

        firstError.GetProperty("sourceKind").GetString().Should().Be("BindingTrace",
            "errors from WPF trace listener should be classified as BindingTrace");
    }

    [Fact]
    public void BindingErrorInfo_Origin_DefaultsToBindingTrace()
    {
        var info = new BindingErrorInfo
        {
            Timestamp = DateTime.UtcNow,
            Message = "test error"
        };

        info.Origin.Should().Be("BindingTrace",
            "default Origin ensures trace listener errors keep existing classification");
    }

    [Fact]
    public void BindingErrorInfo_Origin_CanBeSetToBindingExpression()
    {
        var info = new BindingErrorInfo
        {
            Timestamp = DateTime.UtcNow,
            Message = "live binding error",
            Origin = "BindingExpression"
        };

        info.Origin.Should().Be("BindingExpression",
            "live BindingExpression errors should have their own sourceKind classification");
    }

    [Fact]
    public void GetBindingErrors_ShouldUseOriginAsSourceKind()
    {
        BindingErrorTraceListener.ResetInstance();
        var analyzer = new BindingAnalyzer();

        BindingErrorTraceListener.Instance.TraceEvent(
            null, "System.Windows.Data", TraceEventType.Error, 40,
            "BindingExpression path error: 'TestProp' not found on source object");

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors());
        var firstError = result.GetProperty("errors")[0];

        firstError.GetProperty("sourceKind").GetString().Should().Be("BindingTrace",
            "trace listener errors should use their Origin ('BindingTrace') as sourceKind");
    }

    [StaFact]
    public void GetDataContextChain_ShouldExposeNormalizedSourceKindAndElementId()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var root = new StackPanel { DataContext = new { Name = "Root" } };
        var child = new Button();
        root.Children.Add(child);
        var childId = finder.GenerateElementId(child);

        var result = JsonSerializer.SerializeToElement(analyzer.GetDataContextChain(childId));
        var firstEntry = result.GetProperty("chain")[0];

        firstEntry.GetProperty("diagnosticKind").GetString().Should().Be("DataContextScope");
        firstEntry.GetProperty("elementId").GetString().Should().Be(childId);
        firstEntry.GetProperty("sourceKind").GetString().Should().Be("InheritedDataContext");
    }

    [StaFact]
    public void GetValidationErrors_ShouldExposeNormalizedDiagnosticFields()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var textBox = new TextBox();
        var parent = new StackPanel();
        parent.Children.Add(textBox);
        var parentId = finder.GenerateElementId(parent);

        var binding = new Binding("Text")
        {
            Source = new { Text = "" },
            Mode = BindingMode.OneWay
        };
        textBox.SetBinding(TextBox.TextProperty, binding);

        var expr = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        Validation.MarkInvalid(
            expr!,
            new ValidationError(new ExceptionValidationRule(), expr!) { ErrorContent = "Bad value" });

        var result = JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(parentId));
        var firstError = result.GetProperty("errors")[0];

        firstError.GetProperty("diagnosticKind").GetString().Should().Be("ValidationError");
        firstError.GetProperty("sourceKind").GetString().Should().Be("ValidationRule");
        firstError.TryGetProperty("elementId", out _).Should().BeTrue();
    }
}
