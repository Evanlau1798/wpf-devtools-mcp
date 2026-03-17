using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfDevTools.Tests.TestApp;

public partial class MainWindow
{
    private TextBlock? _eventTraceStatusTextBlock;
    private ListBox? _eventTraceLogListBox;

    private void InitializeEventTraceLab()
    {
        var eventStormButton = new Button
        {
            Name = "EventStormButton",
            Content = "Event Storm Button",
            Width = 180,
            Margin = new Thickness(5)
        };
        eventStormButton.Click += EventStormButton_Click;
        eventStormButton.PreviewMouseLeftButtonDown += EventStormButton_PreviewMouseLeftButtonDown;
        eventStormButton.PreviewMouseLeftButtonUp += EventStormButton_PreviewMouseLeftButtonUp;

        var routedProbeBorder = new Border
        {
            Name = "RoutedProbeBorder",
            Margin = new Thickness(5, 12, 5, 5),
            Padding = new Thickness(18),
            Background = Brushes.AliceBlue,
            BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "MouseDown target for routed event diagnostics"
            }
        };
        routedProbeBorder.MouseDown += RoutedProbeBorder_MouseDown;

        _eventTraceStatusTextBlock = new TextBlock
        {
            Name = "EventTraceStatusTextBlock",
            Margin = new Thickness(5, 12, 5, 5),
            Foreground = Brushes.DarkSlateBlue,
            Text = "Event trace lab ready"
        };

        _eventTraceLogListBox = new ListBox
        {
            Name = "EventTraceLogListBox",
            Margin = new Thickness(5),
            Height = 180
        };
        _eventTraceLogListBox.Items.Add("Event trace lab initialized");

        var content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock
                {
                    Text = "Golden-sample event trace regression coverage",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(5)
                },
                eventStormButton,
                routedProbeBorder,
                _eventTraceStatusTextBlock,
                _eventTraceLogListBox
            }
        };

        var tab = new TabItem
        {
            Name = "EventTraceLabTab",
            Header = "Event Trace Lab",
            Content = content
        };

        RegisterName(tab.Name, tab);
        RegisterName(eventStormButton.Name, eventStormButton);
        RegisterName(routedProbeBorder.Name, routedProbeBorder);
        RegisterName(_eventTraceStatusTextBlock.Name, _eventTraceStatusTextBlock);
        RegisterName(_eventTraceLogListBox.Name, _eventTraceLogListBox);
        MainTabControl.Items.Add(tab);
    }

    private void EventStormButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RecordEventTraceMessage("PreviewMouseLeftButtonDown captured on EventStormButton.");
    }

    private void EventStormButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        RecordEventTraceMessage("PreviewMouseLeftButtonUp captured on EventStormButton.");
    }

    private void EventStormButton_Click(object sender, RoutedEventArgs e)
    {
        RecordEventTraceMessage("Click captured on EventStormButton.");
    }

    private void RoutedProbeBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RecordEventTraceMessage("MouseDown captured on RoutedProbeBorder.");
    }

    private void RecordEventTraceMessage(string message)
    {
        if (DataContext is TestViewModel viewModel)
        {
            viewModel.RecordActionMessage(message);
        }

        if (_eventTraceStatusTextBlock != null)
        {
            _eventTraceStatusTextBlock.Text = message;
        }

        _eventTraceLogListBox?.Items.Insert(0, message);
    }
}
