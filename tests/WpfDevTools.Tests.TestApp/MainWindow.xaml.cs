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
        SetupGeneratedDetailDiagnostics();
        SetupWaitForChangeDiagnostics();
        InitializeEventTraceLab();

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

        var ghostPanel = new Border
        {
            Name = "GhostPanel",
            Margin = new Thickness(5),
            Padding = new Thickness(6),
            Background = Brushes.LightSteelBlue,
            Child = new TextBlock { Text = "Ghost panel" }
        };
        ghostPanel.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(TestViewModel.IsGhostVisible))
        {
            Converter = new BooleanToVisibilityConverter()
        });

        RegisterName(ghostPanel.Name, ghostPanel);
        BasicControlsStackPanel.Children.Add(ghostPanel);
    }

    private void SetupGeneratedDetailDiagnostics()
    {
        var generatedTextFactory1 = new FrameworkElementFactory(typeof(TextBlock));
        generatedTextFactory1.SetValue(FrameworkElement.NameProperty, "GeneratedDetailText1");
        generatedTextFactory1.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
        generatedTextFactory1.SetBinding(TextBlock.TextProperty, new Binding("Nested.DetailText"));

        var generatedTextFactory2 = new FrameworkElementFactory(typeof(TextBlock));
        generatedTextFactory2.SetValue(FrameworkElement.NameProperty, "GeneratedDetailText2");
        generatedTextFactory2.SetBinding(TextBlock.TextProperty, new Binding("Nested.DetailSecondary"));

        var stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
        stackPanelFactory.AppendChild(generatedTextFactory1);
        stackPanelFactory.AppendChild(generatedTextFactory2);

        var detailTemplate = new DataTemplate
        {
            VisualTree = stackPanelFactory
        };

        var detailHost = new ContentControl
        {
            Name = "GeneratedDetailHost",
            Margin = new Thickness(12),
            ContentTemplate = detailTemplate
        };
        detailHost.SetBinding(ContentControl.ContentProperty, new Binding(nameof(TestViewModel.CurrentDetailContext)));

        var detailTab = new TabItem
        {
            Name = "DetailDiagnosticsTab",
            Header = "Detail Diagnostics",
            Content = detailHost
        };

        RegisterName(detailHost.Name, detailHost);
        RegisterName(detailTab.Name, detailTab);
        MainTabControl.Items.Add(detailTab);
    }

    private void SetupWaitForChangeDiagnostics()
    {
        var searchProbeLabel = new TextBlock
        {
            Margin = new Thickness(5, 12, 5, 5),
            Text = "Search Text Probe",
            FontWeight = FontWeights.Bold
        };

        var searchProbeTextBox = new TextBox
        {
            Name = "SearchProbeTextBox",
            Margin = new Thickness(5)
        };
        searchProbeTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(TestViewModel.SearchText))
        {
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        RegisterName(searchProbeTextBox.Name, searchProbeTextBox);

        BasicControlsStackPanel.Children.Add(searchProbeLabel);
        BasicControlsStackPanel.Children.Add(searchProbeTextBox);

        var mirrorTextBox = new TextBox
        {
            Name = "SearchMirrorTextBox",
            Margin = new Thickness(12)
        };
        mirrorTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(TestViewModel.SearchText))
        {
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

        var mirrorTab = new TabItem
        {
            Name = "SearchMirrorTab",
            Header = "Search Mirror",
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Secondary SearchText target",
                        FontWeight = FontWeights.Bold
                    },
                    mirrorTextBox
                }
            }
        };

        RegisterName(mirrorTextBox.Name, mirrorTextBox);
        RegisterName(mirrorTab.Name, mirrorTab);
        MainTabControl.Items.Add(mirrorTab);
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
