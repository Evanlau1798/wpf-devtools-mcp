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

        var nestedPathText = new TextBlock
        {
            Name = "NestedPathText",
            DataContext = new NestedViewModel()
        };
        nestedPathText.SetBinding(TextBlock.TextProperty, new Binding("User.Name"));

        var multiBindingText = new TextBlock
        {
            Name = "FullNameText",
            DataContext = matchingViewModel
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
        root.Children.Add(nameTextBox);
        root.Children.Add(nestedPathText);
        root.Children.Add(multiBindingText);
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
