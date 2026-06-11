using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingAnalyzerAffectedElementsTests
{
    [StaFact]
    public void GetAffectedElements_ShouldReturnSimplePathMatchesWithElementMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var matchingViewModel = new MatchingViewModel();

        var nameTextBox = new TextBox
        {
            Name = "NameTextBox",
            DataContext = matchingViewModel
        };
        nameTextBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
        var matchingElementId = finder.GenerateElementId(nameTextBox);

        var root = new StackPanel();
        root.Children.Add(nameTextBox);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(analyzer.GetAffectedElements("Name", null, rootId, recursive: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("best-effort");
        result.GetProperty("matchStrategy").GetString().Should().Be("simple-path-match");
        result.GetProperty("requiresVerification").GetBoolean().Should().BeTrue();
        result.GetProperty("affectedCount").GetInt32().Should().Be(1);

        var affected = result.GetProperty("affectedElements")[0];
        affected.GetProperty("elementId").GetString().Should().Be(matchingElementId);
        affected.GetProperty("elementType").GetString().Should().Be("TextBox");
        affected.GetProperty("elementName").GetString().Should().Be("NameTextBox");
        affected.GetProperty("bindingPath").GetString().Should().Be("Name");
        affected.GetProperty("currentValue").GetString().Should().Be("Alice");
    }

    [StaFact]
    public void GetAffectedElements_ShouldMatchNestedPathTerminalSegmentsWithHigherConfidence()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var nestedPathText = new TextBlock
        {
            Name = "NestedPathText",
            DataContext = new NestedViewModel()
        };
        nestedPathText.SetBinding(TextBlock.TextProperty, new Binding("User.Name"));

        var root = new StackPanel();
        root.Children.Add(nestedPathText);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("Name", null, rootId, recursive: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("high");
        result.GetProperty("matchStrategy").GetString().Should().Be("terminal-path-match");
        result.GetProperty("affectedCount").GetInt32().Should().Be(1);
        result.GetProperty("affectedElements")[0].GetProperty("elementName").GetString().Should().Be("NestedPathText");
        result.GetProperty("affectedElements")[0].GetProperty("bindingPath").GetString().Should().Be("User.Name");
        result.GetProperty("affectedElements")[0].GetProperty("matchConfidence").GetString().Should().Be("high");
    }

    [StaFact]
    public void GetAffectedElements_ShouldMatchMultiBindingChildPathsWithHigherConfidence()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var multiBindingText = new TextBlock
        {
            Name = "FullNameText",
            DataContext = new MatchingViewModel()
        };
        multiBindingText.SetBinding(TextBlock.TextProperty, new MultiBinding
        {
            Converter = new TestConcatMultiConverter(),
            Bindings =
            {
                new Binding("Name"),
                new Binding("Surname")
            }
        });

        var root = new StackPanel();
        root.Children.Add(multiBindingText);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("Surname", null, rootId, recursive: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("high");
        result.GetProperty("matchStrategy").GetString().Should().Be("multibinding-child-path-match");
        result.GetProperty("affectedCount").GetInt32().Should().Be(1);
        result.GetProperty("affectedElements")[0].GetProperty("elementName").GetString().Should().Be("FullNameText");
        result.GetProperty("affectedElements")[0].GetProperty("bindingPath").GetString().Should().Be("Surname");
        result.GetProperty("affectedElements")[0].GetProperty("matchConfidence").GetString().Should().Be("high");
    }

    [StaFact]
    public void GetAffectedElements_ShouldClassifyInheritedDataContextMatches()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var root = new StackPanel
        {
            DataContext = new MatchingViewModel()
        };

        var child = new TextBox
        {
            Name = "InheritedNameTextBox"
        };
        child.SetBinding(TextBox.TextProperty, new Binding("Name"));
        root.Children.Add(child);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("Name", null, rootId, recursive: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("affectedCount").GetInt32().Should().Be(1);
        result.GetProperty("unsupportedCount").GetInt32().Should().Be(0);
        result.GetProperty("affectedElements")[0].GetProperty("elementName").GetString().Should().Be("InheritedNameTextBox");
        result.GetProperty("affectedElements")[0].GetProperty("sourceClassification").GetString().Should().Be("InheritedDataContext");
    }

    [StaFact]
    public void GetAffectedElements_ShouldExcludeElementNameBindingsWithUnsupportedReason()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var checkBox = new CheckBox
        {
            Name = "SourceCheckBox",
            IsChecked = true
        };
        var target = new TextBlock
        {
            Name = "ElementNameTarget"
        };
        target.SetBinding(TextBlock.TextProperty, new Binding("IsChecked")
        {
            ElementName = "SourceCheckBox"
        });

        var root = new StackPanel();
        root.Children.Add(checkBox);
        root.Children.Add(target);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("IsChecked", null, rootId, recursive: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("low");
        result.GetProperty("matchStrategy").GetString().Should().Be("source-excluded");
        result.GetProperty("affectedCount").GetInt32().Should().Be(0);
        result.GetProperty("unsupportedCount").GetInt32().Should().Be(1);
        result.GetProperty("unsupportedElements")[0].GetProperty("elementName").GetString().Should().Be("ElementNameTarget");
        result.GetProperty("unsupportedElements")[0].GetProperty("sourceClassification").GetString().Should().Be("ElementName");
        result.GetProperty("unsupportedElements")[0].GetProperty("unsupportedReason").GetString().Should().Contain("ElementName");
    }

    [StaFact]
    public void GetAffectedElements_ShouldExcludeRelativeSourceBindingsWithUnsupportedReason()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var selfBoundTextBox = new TextBox
        {
            Name = "SelfBoundTextBox",
            Tag = "Local tag"
        };
        selfBoundTextBox.SetBinding(TextBox.TextProperty, new Binding("Tag")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.Self)
        });

        var root = new StackPanel();
        root.Children.Add(selfBoundTextBox);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("Tag", null, rootId, recursive: true));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("low");
        result.GetProperty("matchStrategy").GetString().Should().Be("source-excluded");
        result.GetProperty("affectedCount").GetInt32().Should().Be(0);
        result.GetProperty("unsupportedCount").GetInt32().Should().Be(1);
        result.GetProperty("unsupportedElements")[0].GetProperty("elementName").GetString().Should().Be("SelfBoundTextBox");
        result.GetProperty("unsupportedElements")[0].GetProperty("sourceClassification").GetString().Should().Be("RelativeSource");
        result.GetProperty("unsupportedElements")[0].GetProperty("unsupportedReason").GetString().Should().Contain("RelativeSource");
    }

    [StaFact]
    public void GetAffectedElements_WithViewModelType_ShouldApplyCoarseFilter()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var matching = new TextBox
        {
            Name = "MatchingNameTextBox",
            DataContext = new MatchingViewModel()
        };
        matching.SetBinding(TextBox.TextProperty, new Binding("Name"));

        var nonMatching = new TextBox
        {
            Name = "OtherNameTextBox",
            DataContext = new DifferentViewModel()
        };
        nonMatching.SetBinding(TextBox.TextProperty, new Binding("Name"));

        var root = new StackPanel();
        root.Children.Add(matching);
        root.Children.Add(nonMatching);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("Name", nameof(MatchingViewModel), rootId, recursive: true));

        result.GetProperty("affectedCount").GetInt32().Should().Be(1);
        result.GetProperty("affectedElements")[0].GetProperty("elementName").GetString().Should().Be("MatchingNameTextBox");
        result.GetProperty("affectedElements")[0].GetProperty("dataContextType").GetString().Should().Be(nameof(MatchingViewModel));
    }

    [StaFact]
    public void GetAffectedElements_WithRecursiveFalse_ShouldNotScanDescendants()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var child = new TextBox
        {
            DataContext = new MatchingViewModel()
        };
        child.SetBinding(TextBox.TextProperty, new Binding("Name"));

        var root = new StackPanel();
        root.Children.Add(child);
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(analyzer.GetAffectedElements("Name", null, rootId, recursive: false));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("affectedCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public void GetAffectedElements_WithLargeRecursiveTree_ShouldReturnTruncationMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var root = new StackPanel();

        for (var index = 0; index < 600; index++)
        {
            var textBox = new TextBox { DataContext = new MatchingViewModel() };
            textBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
            root.Children.Add(textBox);
        }

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("Name", null, finder.GenerateElementId(root), recursive: true));

        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.GetProperty("affectedCount").GetInt32().Should().BeLessOrEqualTo(200);
        result.GetProperty("scanBudget").GetProperty("traversalNodeCount").GetInt32().Should().BeLessOrEqualTo(512);
    }

    [StaFact]
    public void GetAffectedElements_WhenResultLimitIsHit_ShouldStopRecursiveElementAnalysis()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var root = new StackPanel();

        for (var index = 0; index < 600; index++)
        {
            var textBox = new TextBox { DataContext = new MatchingViewModel() };
            textBox.SetBinding(TextBox.TextProperty, new Binding("Name"));
            root.Children.Add(textBox);
        }

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetAffectedElements("Name", null, finder.GenerateElementId(root), recursive: true));
        var budget = result.GetProperty("scanBudget");

        result.GetProperty("affectedCount").GetInt32().Should().Be(200);
        budget.GetProperty("returnedResultCount").GetInt32().Should().Be(200);
        budget.GetProperty("totalResultCount").GetInt32().Should().Be(200);
        budget.GetProperty("traversalNodeCount").GetInt32().Should().BeLessOrEqualTo(201);
    }

    private sealed class MatchingViewModel
    {
        public string Name { get; set; } = "Alice";

        public string Surname { get; set; } = "Johnson";
    }

    private sealed class DifferentViewModel
    {
        public string Name { get; set; } = "Bob";
    }

    private sealed class NestedViewModel
    {
        public UserViewModel User { get; set; } = new();
    }

    private sealed class UserViewModel
    {
        public string Name { get; set; } = "Nested";
    }

    private sealed class TestConcatMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => string.Join(" ", values.OfType<string>());

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
