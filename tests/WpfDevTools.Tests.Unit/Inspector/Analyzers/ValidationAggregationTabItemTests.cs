using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class ValidationAggregationTabItemTests
{
    [StaFact]
    public void GetValidationErrors_OnInactiveTabItem_ShouldAggregateDescendantErrors()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var hostWindow = EnsureHostWindow();
        try
        {
            var tabControl = new TabControl { SelectedIndex = 1 };
            var validatedTab = new TabItem { Header = "Validated" };
            var otherTab = new TabItem { Header = "Other", Content = new TextBlock { Text = "Other" } };
            var formPanel = new StackPanel();
            var firstNameTextBox = CreateInvalidTextBox("First name is required");
            var ageTextBox = CreateInvalidTextBox("Age must be positive");

            formPanel.Children.Add(firstNameTextBox);
            formPanel.Children.Add(ageTextBox);
            validatedTab.Content = formPanel;
            tabControl.Items.Add(validatedTab);
            tabControl.Items.Add(otherTab);
            hostWindow.Content = tabControl;
            hostWindow.Show();
            hostWindow.UpdateLayout();

            var tabId = finder.GenerateElementId(validatedTab);
            var result = JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(tabId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("errorCount").GetInt32().Should().Be(2);
        }
        finally
        {
            hostWindow.Close();
        }
    }

    [StaFact]
    public void GetValidationErrors_OnActiveTabItem_ShouldAggregateDescendantErrors()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var hostWindow = EnsureHostWindow();
        try
        {
            var tabControl = new TabControl { SelectedIndex = 0 };
            var validatedTab = new TabItem { Header = "Validated" };
            var otherTab = new TabItem { Header = "Other", Content = new TextBlock { Text = "Other" } };
            var formPanel = new StackPanel();
            var firstNameTextBox = CreateInvalidTextBox("First name is required");
            var ageTextBox = CreateInvalidTextBox("Age must be positive");

            formPanel.Children.Add(firstNameTextBox);
            formPanel.Children.Add(ageTextBox);
            validatedTab.Content = formPanel;
            tabControl.Items.Add(validatedTab);
            tabControl.Items.Add(otherTab);
            hostWindow.Content = tabControl;
            hostWindow.Show();
            hostWindow.UpdateLayout();

            var tabId = finder.GenerateElementId(validatedTab);
            var result = JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(tabId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("errorCount").GetInt32().Should().Be(2,
                "selected TabItem content is re-parented to ContentPresenter but should still be reachable");
        }
        finally
        {
            hostWindow.Close();
        }
    }

    [StaFact]
    public void GetValidationErrors_OnPreviouslyActiveTabItem_AfterTabSwitch_ShouldStillAggregateErrors()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var hostWindow = EnsureHostWindow();
        try
        {
            var tabControl = new TabControl();
            var validatedTab = new TabItem { Header = "Validated" };
            var otherTab = new TabItem { Header = "Other", Content = new TextBlock { Text = "Other" } };
            var formPanel = new StackPanel();
            var firstNameTextBox = CreateInvalidTextBox("First name is required");
            var ageTextBox = CreateInvalidTextBox("Age must be positive");

            formPanel.Children.Add(firstNameTextBox);
            formPanel.Children.Add(ageTextBox);
            validatedTab.Content = formPanel;
            tabControl.Items.Add(validatedTab);
            tabControl.Items.Add(otherTab);
            hostWindow.Content = tabControl;
            hostWindow.Show();
            hostWindow.UpdateLayout();

            // Start on validated tab, then switch away
            tabControl.SelectedIndex = 0;
            hostWindow.UpdateLayout();
            tabControl.SelectedIndex = 1;
            hostWindow.UpdateLayout();

            var tabId = finder.GenerateElementId(validatedTab);
            var result = JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(tabId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("errorCount").GetInt32().Should().Be(2,
                "TabItem content should be reachable even after tab switch disconnects it from visual tree");
        }
        finally
        {
            hostWindow.Close();
        }
    }

    [StaFact]
    public void GetValidationErrors_ConsistencyBetweenTabItemAndParentScopes()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var hostWindow = EnsureHostWindow();
        try
        {
            var tabControl = new TabControl { SelectedIndex = 1 };
            var validatedTab = new TabItem { Header = "Validated" };
            var otherTab = new TabItem { Header = "Other", Content = new TextBlock { Text = "Other" } };
            var formPanel = new StackPanel();

            formPanel.Children.Add(CreateInvalidTextBox("Error A"));
            formPanel.Children.Add(CreateInvalidTextBox("Error B"));
            validatedTab.Content = formPanel;
            tabControl.Items.Add(validatedTab);
            tabControl.Items.Add(otherTab);
            hostWindow.Content = tabControl;
            hostWindow.Show();
            hostWindow.UpdateLayout();

            var tabId = finder.GenerateElementId(validatedTab);
            var windowId = finder.GenerateElementId(hostWindow);
            var tabResult = JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(tabId));
            var windowResult = JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(windowId));

            var tabErrors = tabResult.GetProperty("errorCount").GetInt32();
            var windowErrors = windowResult.GetProperty("errorCount").GetInt32();

            tabErrors.Should().Be(2, "TabItem should aggregate its descendant errors");
            windowErrors.Should().BeGreaterThanOrEqualTo(tabErrors,
                "root scope should find at least as many errors as TabItem scope");
        }
        finally
        {
            hostWindow.Close();
        }
    }

    [StaFact]
    public void GetValidationErrors_OnRootWindow_ShouldIncludeInactiveTabDescendants()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var hostWindow = EnsureHostWindow();
        try
        {
            var tabControl = new TabControl { SelectedIndex = 1 };
            var validatedTab = new TabItem { Header = "Validated" };
            var otherTab = new TabItem { Header = "Other", Content = new TextBlock { Text = "Other" } };
            var formPanel = new StackPanel();

            formPanel.Children.Add(CreateInvalidTextBox("First field invalid"));
            formPanel.Children.Add(CreateInvalidTextBox("Second field invalid"));
            validatedTab.Content = formPanel;
            tabControl.Items.Add(validatedTab);
            tabControl.Items.Add(otherTab);
            hostWindow.Content = tabControl;
            hostWindow.Show();
            hostWindow.UpdateLayout();

            var windowId = finder.GenerateElementId(hostWindow);
            var result = JsonSerializer.SerializeToElement(analyzer.GetValidationErrors(windowId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("errorCount").GetInt32().Should().Be(2);
        }
        finally
        {
            hostWindow.Close();
        }
    }

    private static Window EnsureHostWindow()
    {
        return new Window
        {
            Width = 480,
            Height = 320
        };
    }

    private static TextBox CreateInvalidTextBox(string errorMessage)
    {
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, new Binding("Text")
        {
            Source = new { Text = string.Empty },
            Mode = BindingMode.OneWay
        });

        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        Validation.MarkInvalid(
            expression!,
            new ValidationError(new ExceptionValidationRule(), expression!)
            {
                ErrorContent = errorMessage
            });

        return textBox;
    }
}
