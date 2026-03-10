using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Tests.TestApp;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestApp;

public sealed class CustomControlsBehaviorTests
{
    [StaFact]
    public void AttachedHighlightColor_ShouldProduceObservableUiSideEffect()
    {
        var textBox = new TextBox();

        CustomTextBox.SetHighlightColor(textBox, "LightYellow");

        textBox.ToolTip.Should().Be("HighlightColor: LightYellow");
        textBox.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.ToString().Should().Be(Colors.LightYellow.ToString());
    }

    [StaFact]
    public void FocusWorkflowControls_ShouldUpdateFocusStatusAndAutomationStatus()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            window.UpdateLayout();

            var startTextBox = window.FindName("FocusStartTextBox").Should().BeOfType<TextBox>().Subject;
            var actionButton = window.FindName("FocusActionButton").Should().BeOfType<Button>().Subject;
            var focusStatus = window.FindName("FocusStatusTextBlock").Should().BeOfType<TextBlock>().Subject;
            var automationStatus = window.FindName("AutomationStatusTextBlock").Should().BeOfType<TextBlock>().Subject;

            startTextBox.Focus();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            focusStatus.Text.Should().Be("FocusStartTextBox focused");

            typeof(Button)
                .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(actionButton, null);

            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            focusStatus.Text.Should().Be("FocusActionButton clicked");
            automationStatus.Text.Should().Be("Focus action invoked");
        }
        finally
        {
            window.Close();
        }
    }
}
