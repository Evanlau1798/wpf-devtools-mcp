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

        var viewModel = new TestViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TestViewModel.LastActionMessage))
            {
                AutomationStatusTextBlock.Text = viewModel.LastActionMessage;
            }
        };
        AutomationStatusTextBlock.Text = viewModel.LastActionMessage;

        InitializePerformanceTab();

        SetupDragAndDrop();

        SetupCustomEvents();
        SetupBindingDiagnosticsSamples();

        InitializeModernTheme();
    }

    private void InitializePerformanceTab()
    {
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
            PerformanceStackPanel.Children.Insert(PerformanceStackPanel.Children.Count - 1, border);
        }
    }

    private void SetupDragAndDrop()
    {
        DragSourceTextBox.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (s is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
            {
                DragDrop.DoDragDrop(textBox, textBox.Text, DragDropEffects.Copy);
            }
        };

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
        CustomButton1.CustomClick += (s, e) =>
        {
            if (DataContext is TestViewModel viewModel)
            {
                viewModel.RecordActionMessage("Custom routed event fired!");
            }
        };
    }

    private void SetupBindingDiagnosticsSamples()
    {
        var multiBindingTextBlock = new TextBlock
        {
            Name = "MultiBindingTextBlock",
            Margin = new Thickness(5)
        };

        multiBindingTextBlock.SetBinding(TextBlock.TextProperty, new MultiBinding
        {
            Converter = new ConcatMultiConverter(),
            Bindings =
            {
                new Binding(nameof(TestViewModel.FirstName)),
                new Binding(nameof(TestViewModel.LastName))
            }
        });

        BasicControlsStackPanel.Children.Add(multiBindingTextBlock);
    }

    private void FocusWorkflowElement_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            FocusStatusTextBlock.Text = $"{element.Name} focused";
        }
    }

    private void FocusActionButton_Click(object sender, RoutedEventArgs e)
    {
        FocusStatusTextBlock.Text = "FocusActionButton clicked";
        if (DataContext is TestViewModel viewModel)
        {
            viewModel.RecordActionMessage("Focus action invoked");
        }
    }
}
