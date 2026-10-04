using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("InteractionState")]
public sealed class CollectionSelectionBindingTests
{
    [StaFact]
    public void ClickRow_WithExistingSelection_ShouldReplaceSelectionAndUpdateBinding()
    {
        using var finder = new ElementFinder();
        var state = new SelectionState { Selected = "First" };
        var grid = new DataGrid { ItemsSource = new[] { "First", "Second" },
            SelectionMode = DataGridSelectionMode.Extended };
        grid.SetBinding(Selector.SelectedItemProperty,
            new Binding(nameof(SelectionState.Selected)) { Source = state, Mode = BindingMode.TwoWay });
        var window = new Window { Width = 300, Height = 200, Content = grid, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            var first = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var second = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
            grid.SelectedItem.Should().Be("First");
            first.IsSelected.Should().BeTrue();
            var result = JsonSerializer.SerializeToElement(
                new InteractionAnalyzer(finder).ClickElement(finder.GenerateElementId(second)));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            grid.SelectedItem.Should().Be("Second");
            grid.SelectedItems.Cast<object>().Should().ContainSingle().Which.Should().Be("Second");
            first.IsSelected.Should().BeFalse();
            state.Selected.Should().Be("Second");
            BindingOperations.IsDataBound(grid, Selector.SelectedItemProperty).Should().BeTrue();
        }
        finally { window.Close(); }
    }

    public sealed class SelectionState
    {
        public string? Selected { get; set; }
    }
}
