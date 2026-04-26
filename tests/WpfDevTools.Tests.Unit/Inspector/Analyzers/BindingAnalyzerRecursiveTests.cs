using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class BindingAnalyzerRecursiveTests
{
    [StaFact]
    public void GetBindings_WithRecursiveTrue_ShouldReturnBindingsFromDescendants()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var child1 = new TextBox();
        child1.SetBinding(TextBox.TextProperty, new Binding("Name"));

        var child2 = new TextBox();
        child2.SetBinding(TextBox.TextProperty, new Binding("Age"));

        var parent = new StackPanel();
        parent.Children.Add(child1);
        parent.Children.Add(child2);

        var parentId = finder.GenerateElementId(parent);

        var result = analyzer.GetBindings(parentId, recursive: true);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        var bindings = json.GetProperty("bindings");
        bindings.GetArrayLength().Should().BeGreaterThanOrEqualTo(2,
            "recursive should collect bindings from child TextBox elements");
    }

    [StaFact]
    public void GetBindings_WithRecursiveFalse_ShouldReturnOnlyElementBindings()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var child = new TextBox();
        child.SetBinding(TextBox.TextProperty, new Binding("ChildProp"));

        var parent = new StackPanel();
        parent.Children.Add(child);

        var parentId = finder.GenerateElementId(parent);

        var result = analyzer.GetBindings(parentId, recursive: false);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        var bindings = json.GetProperty("bindings");
        bindings.GetArrayLength().Should().Be(0,
            "StackPanel itself has no bindings");
    }

    [StaFact]
    public void GetBindings_WithRecursiveTrue_ShouldIncludeElementIdInResults()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, new Binding("Value"));

        var parent = new StackPanel();
        parent.Children.Add(textBox);

        var parentId = finder.GenerateElementId(parent);

        var result = analyzer.GetBindings(parentId, recursive: true);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        var bindings = json.GetProperty("bindings");
        bindings.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        var firstBinding = bindings[0];
        firstBinding.TryGetProperty("elementId", out _).Should().BeTrue(
            "recursive results should include elementId to identify which element owns the binding");
        firstBinding.TryGetProperty("elementType", out _).Should().BeTrue(
            "recursive results should include elementType for context");
    }

    [StaFact]
    public void GetBindings_WithRecursiveTrue_DeepHierarchy_ShouldTraverseAllLevels()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var deepChild = new TextBox();
        deepChild.SetBinding(TextBox.TextProperty, new Binding("DeepValue"));

        var innerPanel = new StackPanel();
        innerPanel.Children.Add(deepChild);

        var outerPanel = new StackPanel();
        outerPanel.Children.Add(innerPanel);

        var rootId = finder.GenerateElementId(outerPanel);

        var result = analyzer.GetBindings(rootId, recursive: true);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        var bindings = json.GetProperty("bindings");
        bindings.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "should find binding in deeply nested child");

        var paths = Enumerable.Range(0, bindings.GetArrayLength())
            .Select(i => bindings[i].GetProperty("path").GetString())
            .ToList();
        paths.Should().Contain("DeepValue");
    }

    [StaFact]
    public void GetBindings_WithRecursiveBudgetsExceeded_ShouldReturnBoundedResultsAndTruncationMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var first = new TextBox();
        first.SetBinding(TextBox.TextProperty, new Binding("FirstValue"));
        first.SetBinding(FrameworkElement.TagProperty, new Binding("FirstTag"));

        var second = new TextBox();
        second.SetBinding(TextBox.TextProperty, new Binding("SecondValue"));

        var root = new StackPanel();
        root.Children.Add(first);
        root.Children.Add(second);

        var rootId = finder.GenerateElementId(root);

        var result = analyzer.GetBindings(
            rootId,
            recursive: true,
            maxTraversalNodes: 2,
            maxResults: 1);

        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("bindings").GetArrayLength().Should().Be(1);
        json.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var metadata = json.GetProperty("truncationMetadata");
        metadata.GetProperty("maxTraversalNodes").GetInt32().Should().Be(2);
        metadata.GetProperty("maxResults").GetInt32().Should().Be(1);
        metadata.GetProperty("reasons").EnumerateArray()
            .Select(reason => reason.GetString())
            .Should().Contain("TraversalNodeLimit")
            .And.Contain("ResultLimit");
    }

    [StaFact]
    public void GetBindings_WithRecursiveTrue_NoBindingsInTree_ShouldReturnEmptyList()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var parent = new StackPanel();
        parent.Children.Add(new Button { Content = "No bindings" });
        parent.Children.Add(new TextBlock { Text = "Also no bindings" });

        var parentId = finder.GenerateElementId(parent);

        var result = analyzer.GetBindings(parentId, recursive: true);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("bindings").GetArrayLength().Should().Be(0);
    }

    [StaFact]
    public void GetBindings_WithRecursiveNull_ShouldBehaveLikeNonRecursive()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);

        var child = new TextBox();
        child.SetBinding(TextBox.TextProperty, new Binding("ChildProp"));

        var parent = new StackPanel();
        parent.Children.Add(child);

        var parentId = finder.GenerateElementId(parent);

        // Default behavior (no recursive param) should only inspect the element itself
        var result = analyzer.GetBindings(parentId);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("bindings").GetArrayLength().Should().Be(0,
            "default should not recurse into children");
    }
}
