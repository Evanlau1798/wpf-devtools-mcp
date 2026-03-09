using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Tests.TestApp;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestApp;

public sealed class MainWindowAutomationStatusTests
{
    [StaFact]
    public void CustomButtonClick_ShouldUpdateAutomationStatusText()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            window.UpdateLayout();

            var viewModel = window.DataContext.Should().BeOfType<TestViewModel>().Subject;
            var button = window.FindName("CustomButton1").Should().BeOfType<CustomButton>().Subject;
            var statusText = window.FindName("AutomationStatusTextBlock").Should().BeOfType<TextBlock>().Subject;

            typeof(CustomButton)
                .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button, null);

            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            viewModel.LastActionMessage.Should().Be("Custom routed event fired!");
            statusText.Text.Should().Be("Custom routed event fired!");
        }
        finally
        {
            window.Close();
        }
    }
}
