using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingMismatchAnalyzerTests
{
    [StaFact]
    public void GetBindingMismatches_ShouldReportTypeMismatchWithoutConverter()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        button.SetBinding(Control.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
        {
            Source = new BindingMismatchSource { IsEnabled = true }
        });
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId));
        var mismatch = result.GetProperty("mismatches")[0];

        mismatch.GetProperty("propertyName").GetString().Should().Be("Background");
        mismatch.GetProperty("bindingPath").GetString().Should().Be("IsEnabled");
        mismatch.GetProperty("targetType").GetString().Should().Be("Brush");
        mismatch.GetProperty("sourceType").GetString().Should().Be("Boolean");
        mismatch.GetProperty("diagnosis").GetString().Should().Be("TypeMismatch");
        mismatch.GetProperty("severity").GetString().Should().Be("Warning");
        mismatch.GetProperty("converter").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [StaFact]
    public void GetBindingMismatches_ShouldReportTypeMismatchWithConverterAsInfo()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        button.SetBinding(Control.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
        {
            Source = new BindingMismatchSource { IsEnabled = true },
            Converter = new BooleanToBrushConverter()
        });
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId));
        var mismatch = result.GetProperty("mismatches")[0];

        mismatch.GetProperty("diagnosis").GetString().Should().Be("TypeMismatchWithConverter");
        mismatch.GetProperty("severity").GetString().Should().Be("Info");
        mismatch.GetProperty("converter").GetString().Should().Be(nameof(BooleanToBrushConverter));
    }

    [StaFact]
    public void GetBindingMismatches_ShouldReportPathMismatchInsteadOfTypeMismatch()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, new Binding("Missing.Path")
        {
            Source = new BindingMismatchSource()
        });
        var elementId = finder.GenerateElementId(textBox);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId));
        var mismatch = result.GetProperty("mismatches")[0];

        mismatch.GetProperty("diagnosis").GetString().Should().Be("PathMismatch");
        mismatch.GetProperty("bindingPath").GetString().Should().Be("Missing.Path");
        mismatch.GetProperty("sourceType").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [StaFact]
    public void GetBindingMismatches_ShouldReportNullableToNonNullableMismatch()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button();
        button.SetBinding(UIElement.OpacityProperty, new Binding(nameof(BindingMismatchSource.MaybeOpacity))
        {
            Source = new BindingMismatchSource { MaybeOpacity = 0.5 }
        });
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId));
        var mismatch = result.GetProperty("mismatches")[0];

        mismatch.GetProperty("diagnosis").GetString().Should().Be("NullabilityMismatch");
        mismatch.GetProperty("targetType").GetString().Should().Be("Double");
        mismatch.GetProperty("sourceType").GetString().Should().Be("Nullable<Double>");
    }

    [StaFact]
    public void GetBindingMismatches_ShouldExcludeUnnamedFrameworkTemplateElementsByDefault()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button
        {
            Template = BuildTemplateWithUnnamedFrameworkHost()
        };

        button.ApplyTemplate();
        var border = AttachUnnamedFrameworkMismatch(button);
        var elementId = finder.GenerateElementId(border);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId));

        result.GetProperty("mismatchCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void GetBindingMismatches_ShouldIncludeUnnamedFrameworkTemplateElementsWhenRequested()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var button = new Button
        {
            Template = BuildTemplateWithUnnamedFrameworkHost()
        };

        button.ApplyTemplate();
        var border = AttachUnnamedFrameworkMismatch(button);
        var elementId = finder.GenerateElementId(border);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId, includeFramework: true));
        var mismatch = result.GetProperty("mismatches")[0];

        mismatch.GetProperty("origin").GetString().Should().Be("FrameworkTemplate");
        mismatch.GetProperty("elementType").GetString().Should().Be("Border");
        mismatch.GetProperty("diagnosis").GetString().Should().Be("TypeMismatch");
    }

    [StaFact]
    public void GetBindingMismatches_ShouldKeepUserCodeMismatchWhenFrameworkFilteringIsEnabled()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var root = new Grid();
        var button = new Button
        {
            Name = "UserMismatchButton",
            Template = BuildTemplateWithUnnamedFrameworkHost()
        };
        button.SetBinding(Control.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
        {
            Source = new BindingMismatchSource { IsEnabled = true }
        });
        root.Children.Add(button);

        button.ApplyTemplate();
        AttachUnnamedFrameworkMismatch(button);
        var elementId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId, recursive: true));

        result.GetProperty("mismatches").EnumerateArray()
            .Should()
            .ContainSingle(item =>
                item.GetProperty("elementName").GetString() == "UserMismatchButton" &&
                item.GetProperty("origin").GetString() == "UserCode");
    }

    private static ControlTemplate BuildTemplateWithUnnamedFrameworkHost()
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        template.VisualTree = borderFactory;
        return template;
    }

    private static Border AttachUnnamedFrameworkMismatch(Button button)
    {
        var border = FindDescendant<Border>(button);
        border.Should().NotBeNull();
        border!.SetBinding(Border.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
        {
            Source = new BindingMismatchSource { IsEnabled = true }
        });
        return border;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var descendant in DependencyObjectTraversal.EnumerateDescendantsAndSelf(root))
        {
            if (descendant is T match && !ReferenceEquals(match, root))
            {
                return match;
            }
        }

        return null;
    }

    private sealed class BindingMismatchSource
    {
        public bool IsEnabled { get; set; }

        public double? MaybeOpacity { get; set; }
    }

    private sealed class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Brushes.Green;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
