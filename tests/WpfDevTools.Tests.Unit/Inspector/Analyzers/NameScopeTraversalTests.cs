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

    private static Window EnsureWindowWithNameScope()
    {
        var window = new Window();
        NameScope.SetNameScope(window, new NameScope());
        return window;
    }
}
