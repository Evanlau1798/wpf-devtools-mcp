using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfDevTools.Tests.TestApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set DataContext to TestViewModel
        DataContext = new TestViewModel();

        // Initialize performance test elements
        InitializePerformanceTab();

        // Setup drag & drop handlers
        SetupDragAndDrop();

        // Setup custom event handlers
        SetupCustomEvents();
    }

    private void InitializePerformanceTab()
    {
        // Add 100 elements to test performance tools
        for (int i = 0; i < 100; i++)
        {
            var border = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Padding = new Thickness(5)
            };

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            stackPanel.Children.Add(new TextBlock
            {
                Text = $"Element {i + 1}: ",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 5, 0)
            });

            stackPanel.Children.Add(new TextBox
            {
                Text = $"Value {i + 1}",
                Width = 100,
                Margin = new Thickness(0, 0, 5, 0)
            });

            stackPanel.Children.Add(new Button
            {
                Content = "Click",
                Width = 60
            });

            border.Child = stackPanel;
            PerformanceStackPanel.Children.Add(border);
        }
    }

    private void SetupDragAndDrop()
    {
        // Drag source
        DragSourceTextBox.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (s is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
            {
                DragDrop.DoDragDrop(textBox, textBox.Text, DragDropEffects.Copy);
            }
        };

        // Drop target
        DropTargetTextBox.DragEnter += (s, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        };

        DropTargetTextBox.Drop += (s, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var text = e.Data.GetData(DataFormats.Text) as string;
                if (s is TextBox textBox)
                {
                    textBox.Text = text ?? "";
                }
            }
            e.Handled = true;
        };
    }

    private void SetupCustomEvents()
    {
        // Handle custom routed event
        CustomButton1.CustomClick += (s, e) =>
        {
            MessageBox.Show("Custom routed event fired!", "Custom Event", MessageBoxButton.OK, MessageBoxImage.Information);
        };
    }
}
