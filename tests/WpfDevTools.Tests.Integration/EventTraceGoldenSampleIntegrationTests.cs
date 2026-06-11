using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Tests.TestApp;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class EventTraceGoldenSampleIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public EventTraceGoldenSampleIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TraceRoutedEvents_WithGoldenSampleCheckBoxClick_ShouldCaptureClickEvent()
    {
        var dispatcher = new WpfDevTools.Inspector.Host.RequestDispatcher(new WpfDevTools.Shared.Utilities.FileLogger());
        MainWindow? window = null;

        try
        {
            CheckBox? checkBox = null;
            TextBlock? highlightTextBlock = null;

            await _fixture.RunOnUIThread(async () =>
            {
                window = new MainWindow();
                Application.Current.MainWindow = window;
                window.Show();
                window.Activate();
                window.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                checkBox = window.FindName("EnableHighlightCheckBox") as CheckBox;
                highlightTextBlock = window.FindName("HighlightTextBlock") as TextBlock;
            });

            checkBox.Should().NotBeNull();
            highlightTextBlock.Should().NotBeNull();

            var elementFinder = GetSharedElementFinder(dispatcher);
            var checkBoxId = elementFinder.GenerateElementId(checkBox!);
            checkBoxId.Should().NotBeNullOrEmpty();

            var startResponse = await dispatcher.DispatchAsync(
                new InspectorRequest
                {
                    Id = "trace-start-1",
                    Method = "trace_routed_events",
                    Params = JsonSerializer.SerializeToElement(new
                    {
                        elementId = checkBoxId,
                        eventName = "Click",
                        mode = "start",
                        duration = 2000
                    }),
                    CorrelationId = "corr-trace-start-1"
                },
                CancellationToken.None);

            startResponse.Error.Should().BeNull();
            startResponse.Result!.Value.GetProperty("success").GetBoolean().Should().BeTrue();

            var clickResponse = await dispatcher.DispatchAsync(
                new InspectorRequest
                {
                    Id = "click-1",
                    Method = "click_element",
                    Params = JsonSerializer.SerializeToElement(new { elementId = checkBoxId }),
                    CorrelationId = "corr-click-1"
                },
                CancellationToken.None);

            clickResponse.Error.Should().BeNull();
            clickResponse.Result!.Value.GetProperty("success").GetBoolean().Should().BeTrue();

            var visualState = _fixture.RunOnUIThread(() =>
            {
                return new
                {
                    IsChecked = checkBox!.IsChecked,
                    Foreground = (highlightTextBlock!.Foreground as SolidColorBrush)?.Color.ToString()
                };
            });

            visualState.IsChecked.Should().BeTrue();
            visualState.Foreground.Should().Be(Colors.Red.ToString());

            var getResponse = await ConditionWaiter.WaitForAsync(
                () => dispatcher.DispatchAsync(
                    new InspectorRequest
                    {
                        Id = "trace-get-1",
                        Method = "trace_routed_events",
                        Params = JsonSerializer.SerializeToElement(new { mode = "get" }),
                        CorrelationId = "corr-trace-get-1"
                    },
                    CancellationToken.None),
                response => response.Result.HasValue
                    && response.Result.Value.TryGetProperty("eventCount", out var eventCount)
                    && eventCount.GetInt32() > 0,
                TimeSpan.FromSeconds(2),
                "Timed out waiting for trace_routed_events(mode='get') to return the captured golden-sample click event.");

            getResponse.Error.Should().BeNull();
            getResponse.Result!.Value.GetProperty("success").GetBoolean().Should().BeTrue();
            getResponse.Result.Value.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0);
        }
        finally
        {
            dispatcher.Dispose();
            if (window != null)
            {
                _fixture.RunOnUIThread(() => window.Close());
            }
        }
    }

    private static WpfDevTools.Inspector.Utilities.ElementFinder GetSharedElementFinder(
        WpfDevTools.Inspector.Host.RequestDispatcher dispatcher)
    {
        var field = typeof(WpfDevTools.Inspector.Host.RequestDispatcher)
            .GetField("_elementFinder", BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        return field!.GetValue(dispatcher)
            .Should().BeOfType<WpfDevTools.Inspector.Utilities.ElementFinder>()
            .Subject;
    }
}
