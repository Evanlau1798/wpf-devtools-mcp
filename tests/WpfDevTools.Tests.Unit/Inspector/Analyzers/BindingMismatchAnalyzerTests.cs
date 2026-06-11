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
    public void GetBindingMismatches_ShouldClassifyNamedPartTemplateElementsAsFrameworkTemplate()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var textBox = new TextBox { Text = "sample" };
        PrepareControl(textBox);
        var part = FindNamedDescendant<FrameworkElement>(textBox, "PART_ContentHost");
        part.Should().NotBeNull();
        AttachFrameworkMismatch(part!);
        var elementId = finder.GenerateElementId(part!);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId, includeFramework: true));
        var mismatch = result.GetProperty("mismatches")[0];

        mismatch.GetProperty("origin").GetString().Should().Be("FrameworkTemplate");
        mismatch.GetProperty("elementName").GetString().Should().Be("PART_ContentHost");
    }

    [StaFact]
    public void GetBindingMismatches_ShouldClassifyDataGridTemplatePartNamesAsFrameworkTemplate()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var dataGrid = new DataGrid
        {
            ItemsSource = new[] { new { Name = "one" } },
            AutoGenerateColumns = true
        };
        PrepareControl(dataGrid);
        var part = FindNamedDescendant<FrameworkElement>(dataGrid, "DG_ScrollViewer");
        part.Should().NotBeNull();
        AttachFrameworkMismatch(part!);
        var elementId = finder.GenerateElementId(part!);

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingMismatches(elementId, includeFramework: true));
        var mismatch = result.GetProperty("mismatches")[0];

        mismatch.GetProperty("origin").GetString().Should().Be("FrameworkTemplate");
        mismatch.GetProperty("elementName").GetString().Should().Be("DG_ScrollViewer");
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

    [StaFact]
    public void GetBindingMismatches_WithLargeRecursiveTree_ShouldReturnTruncationMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var root = new StackPanel();

        for (var index = 0; index < 600; index++)
        {
            var button = new Button();
            button.SetBinding(Control.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
            {
                Source = new BindingMismatchSource { IsEnabled = true }
            });
            root.Children.Add(button);
        }

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingMismatches(finder.GenerateElementId(root), recursive: true));

        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.GetProperty("mismatchCount").GetInt32().Should().BeLessOrEqualTo(200);
        result.GetProperty("scanBudget").GetProperty("traversalNodeCount").GetInt32().Should().BeLessOrEqualTo(512);
    }

    [StaFact]
    public void GetBindingMismatches_WhenResultLimitIsHit_ShouldStopRecursiveElementAnalysis()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var root = new StackPanel();

        for (var index = 0; index < 600; index++)
        {
            var button = new Button();
            button.SetBinding(Control.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
            {
                Source = new BindingMismatchSource { IsEnabled = true }
            });
            root.Children.Add(button);
        }

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingMismatches(finder.GenerateElementId(root), recursive: true));
        var budget = result.GetProperty("scanBudget");

        result.GetProperty("mismatchCount").GetInt32().Should().Be(200);
        budget.GetProperty("returnedResultCount").GetInt32().Should().Be(200);
        budget.GetProperty("totalResultCount").GetInt32().Should().Be(200);
        budget.GetProperty("traversalNodeCount").GetInt32().Should().BeLessOrEqualTo(201);
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

    private static void AttachFrameworkMismatch(FrameworkElement element)
    {
        if (element is Border border)
        {
            border.SetBinding(Border.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
            {
                Source = new BindingMismatchSource { IsEnabled = true }
            });
            return;
        }

        if (element is Control control)
        {
            control.SetBinding(Control.BackgroundProperty, new Binding(nameof(BindingMismatchSource.IsEnabled))
            {
                Source = new BindingMismatchSource { IsEnabled = true }
            });
            return;
        }

        throw new NotSupportedException($"Unsupported framework part type: {element.GetType().FullName}");
    }

    private static T? FindNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        foreach (var descendant in DependencyObjectTraversal.EnumerateDescendantsAndSelf(root))
        {
            if (descendant is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
            {
                return match;
            }
        }

        return null;
    }

    private static void PrepareControl(Control control)
    {
        var host = new Window
        {
            Content = control,
            Width = 640,
            Height = 480,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false
        };
        host.Show();
        control.ApplyTemplate();
        control.UpdateLayout();
        host.Content = null;
        host.Close();
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
