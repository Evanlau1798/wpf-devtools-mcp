using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyExpressionRollbackTests
{
    [StaFact]
    public void ClearValue_ShouldRestoreCapturedBindingExpression_AfterSetValueReplacedIt()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var textBox = new TextBox();
        BindingOperations.SetBinding(textBox, TextBox.TextProperty, new Binding(nameof(SampleViewModel.Name))
        {
            Source = new SampleViewModel { Name = "Alice" },
            Mode = BindingMode.TwoWay
        });
        var elementId = finder.GenerateElementId(textBox);

        var setResult = JsonSerializer.SerializeToElement(analyzer.SetValue("Text", "Bob", elementId));
        var clearResult = JsonSerializer.SerializeToElement(analyzer.ClearValue("Text", elementId));
        var valueSourceResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Text", elementId));

        setResult.GetProperty("success").GetBoolean().Should().BeTrue();
        setResult.GetProperty("replacedExpression").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("expressionKind").GetString().Should().Be("Binding");
        clearResult.GetProperty("newValue").GetString().Should().Be("Alice");
        valueSourceResult.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        valueSourceResult.GetProperty("currentValue").GetString().Should().Be("Alice");
    }

    [StaFact]
    public void ClearValue_ShouldPreserveDataContextBindingUpdates_AfterRestoringCapturedExpression()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var viewModel = new NotifyingViewModel { Name = "Alice" };
        var textBox = new TextBox
        {
            DataContext = viewModel
        };
        BindingOperations.SetBinding(textBox, TextBox.TextProperty, new Binding(nameof(NotifyingViewModel.Name))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        var elementId = finder.GenerateElementId(textBox);

        var setResult = JsonSerializer.SerializeToElement(analyzer.SetValue("Text", "Bob", elementId));
        var clearResult = JsonSerializer.SerializeToElement(analyzer.ClearValue("Text", elementId));
        viewModel.Name = "Carol";
        var valueSourceResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Text", elementId));

        setResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
        valueSourceResult.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        valueSourceResult.GetProperty("currentValue").GetString().Should().Be("Carol");
    }

    [StaFact]
    public void ClearValue_ShouldRestoreVisibilityBindingExpression_AfterSetValueReplacedIt()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var viewModel = new VisibilityViewModel { IsGhostVisible = false };
        var border = new Border
        {
            DataContext = viewModel
        };
        border.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(VisibilityViewModel.IsGhostVisible))
        {
            Converter = new BooleanToVisibilityConverter()
        });
        var elementId = finder.GenerateElementId(border);

        var beforeResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Visibility", elementId));
        var setResult = JsonSerializer.SerializeToElement(analyzer.SetValue("Visibility", "Visible", elementId));
        var clearResult = JsonSerializer.SerializeToElement(analyzer.ClearValue("Visibility", elementId));
        var afterResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Visibility", elementId));

        beforeResult.GetProperty("success").GetBoolean().Should().BeTrue();
        beforeResult.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        beforeResult.GetProperty("currentValue").GetString().Should().Be("Collapsed");
        setResult.GetProperty("success").GetBoolean().Should().BeTrue();
        setResult.GetProperty("replacedExpression").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("expressionKind").GetString().Should().Be("Binding");
        afterResult.GetProperty("success").GetBoolean().Should().BeTrue();
        afterResult.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        afterResult.GetProperty("currentValue").GetString().Should().Be("Collapsed");
    }

    [StaFact]
    public void ClearValue_ShouldFallbackToRequestIdentity_WhenObjectKeyRollbackTokenIsUnavailable()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var viewModel = new VisibilityViewModel { IsGhostVisible = false };
        var border = new Border
        {
            DataContext = viewModel
        };
        border.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(VisibilityViewModel.IsGhostVisible))
        {
            Converter = new BooleanToVisibilityConverter()
        });
        var elementId = finder.GenerateElementId(border);

        var setResult = JsonSerializer.SerializeToElement(analyzer.SetValue("Visibility", "Visible", elementId));
        HasLatestRequestRollbackToken(elementId, "Visibility").Should().BeTrue();
        RemoveLatestRollbackTokenObjectKeys();

        var clearResult = JsonSerializer.SerializeToElement(analyzer.ClearValue("Visibility", elementId));
        var afterResult = JsonSerializer.SerializeToElement(analyzer.GetValueSource("Visibility", elementId));

        setResult.GetProperty("success").GetBoolean().Should().BeTrue();
        setResult.GetProperty("capturedRollbackExpression").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
        afterResult.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        afterResult.GetProperty("currentValue").GetString().Should().Be("Collapsed");
    }

    [StaFact]
    public void ResolveBindingBaseForCapture_ShouldFallbackToParentBindingBase_WhenBindingBaseUnavailable()
    {
        var border = new Border();
        border.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(VisibilityViewModel.IsGhostVisible))
        {
            Source = new VisibilityViewModel { IsGhostVisible = false },
            Converter = new BooleanToVisibilityConverter()
        });

        var bindingExpression = BindingOperations.GetBindingExpressionBase(border, UIElement.VisibilityProperty);

        var resolvedBindingBase = DependencyPropertyAnalyzer.ResolveBindingBaseForCapture(null, bindingExpression);

        resolvedBindingBase.Should().NotBeNull();
        resolvedBindingBase.Should().BeOfType<Binding>();
        ((Binding)resolvedBindingBase!).Path?.Path.Should().Be(nameof(VisibilityViewModel.IsGhostVisible));
    }

    private sealed class SampleViewModel
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class NotifyingViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class VisibilityViewModel
    {
        public bool IsGhostVisible { get; init; }
    }

    private static void RemoveLatestRollbackTokenObjectKeys()
    {
        var field = typeof(DependencyPropertyAnalyzer).GetField(
            "_latestRollbackTokens",
            BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();

        var dictionary = field!.GetValue(null) as IDictionary;
        dictionary.Should().NotBeNull();
        dictionary!.Clear();
    }

    private static bool HasLatestRequestRollbackToken(string elementId, string propertyName)
    {
        var field = typeof(DependencyPropertyAnalyzer).GetField(
            "_latestRollbackTokensByRequestKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();

        var dictionary = field!.GetValue(null) as IDictionary;
        dictionary.Should().NotBeNull();

        var key = $"{elementId}::{propertyName}";
        return dictionary!.Contains(key);
    }
}
