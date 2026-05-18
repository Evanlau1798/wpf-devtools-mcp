using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class NameScopeTraversalTests
{
    [StaFact]
    public void GetNameScope_OnRootWindow_ShouldIncludeInactiveTabNamedElements()
    {
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var window = EnsureWindowWithNameScope();
        try
        {
            var tabControl = new TabControl { SelectedIndex = 1 };
            var inactiveTab = new TabItem { Header = "Inactive" };
            var activeTab = new TabItem { Header = "Active", Content = new TextBlock { Text = "Active" } };
            var inactiveButton = new Button { Name = "InactiveTabButton", Content = "Inactive" };

            inactiveTab.Content = inactiveButton;
            tabControl.Items.Add(inactiveTab);
            tabControl.Items.Add(activeTab);
            window.Content = tabControl;
            window.RegisterName(inactiveButton.Name, inactiveButton);
            window.Show();
            window.UpdateLayout();

            var windowId = finder.GenerateElementId(window);
            var result = JsonSerializer.SerializeToElement(analyzer.GetNameScope(windowId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("namedElements")
                .EnumerateArray()
                .Select(element => element.GetProperty("name").GetString())
                .Should()
                .Contain("InactiveTabButton");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetNameScope_WithTraversalBudget_ShouldReportTruncation()
    {
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var window = EnsureWindowWithNameScope();
        try
        {
            var panel = new StackPanel();
            for (var index = 0; index < 5; index++)
            {
                var button = new Button { Name = $"BudgetButton{index}", Content = index.ToString() };
                panel.Children.Add(button);
                window.RegisterName(button.Name, button);
            }

            window.Content = panel;
            window.Show();
            window.UpdateLayout();

            var windowId = finder.GenerateElementId(window);
            var result = JsonSerializer.SerializeToElement(analyzer.GetNameScope(windowId, maxNodes: 2));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("maxTraversalNodes").GetInt32().Should().Be(2);
            result.GetProperty("traversalNodeCount").GetInt32().Should().Be(2);
            result.GetProperty("traversalTruncated").GetBoolean().Should().BeTrue();
            result.GetProperty("namedElements")
                .EnumerateArray()
                .Select(element => element.GetProperty("name").GetString())
                .Should()
                .NotContain("BudgetButton4");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetNameScope_WithDeepSubtreeWithinNodeBudget_ShouldNotDepthTruncate()
    {
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var window = EnsureWindowWithNameScope();
        try
        {
            const string deepButtonName = "DeepScopeButton";
            var root = new StackPanel();
            var current = root;

            for (var depth = 0; depth < 64; depth++)
            {
                var next = new StackPanel();
                current.Children.Add(next);
                current = next;
            }

            var button = new Button { Name = deepButtonName, Content = "Deep" };
            current.Children.Add(button);
            window.Content = root;
            window.RegisterName(button.Name, button);
            window.Show();
            window.UpdateLayout();

            var windowId = finder.GenerateElementId(window);
            var result = JsonSerializer.SerializeToElement(analyzer.GetNameScope(windowId, maxNodes: 100));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("traversalTruncated").GetBoolean().Should().BeFalse();
            result.GetProperty("namedElements")
                .EnumerateArray()
                .Select(element => element.GetProperty("name").GetString())
                .Should()
                .Contain(deepButtonName);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetNameScope_WithoutNameScope_ShouldNotTraverseSubtree()
    {
        var finder = new ElementFinder();
        var analyzer = new VisualTreeAnalyzer(finder);
        var root = new CountingLogicalElement(Enumerable
            .Range(0, 5)
            .Select(_ => new Button())
            .Cast<DependencyObject>());
        var rootId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(analyzer.GetNameScope(rootId, maxNodes: 2));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasNameScope").GetBoolean().Should().BeFalse();
        result.GetProperty("traversalNodeCount").GetInt32().Should().Be(0);
        result.GetProperty("traversalTruncated").GetBoolean().Should().BeFalse();
        root.YieldedLogicalChildrenCount.Should().Be(0);
    }

    private static Window EnsureWindowWithNameScope()
    {
        var window = new Window();
        NameScope.SetNameScope(window, new NameScope());
        return window;
    }

    private sealed class CountingLogicalElement : FrameworkElement
    {
        private readonly DependencyObject[] _children;

        public CountingLogicalElement(IEnumerable<DependencyObject> children)
        {
            _children = children.ToArray();
        }

        public int YieldedLogicalChildrenCount { get; private set; }

        protected override System.Collections.IEnumerator LogicalChildren
        {
            get
            {
                foreach (var child in _children)
                {
                    YieldedLogicalChildrenCount++;
                    yield return child;
                }
            }
        }
    }
}
