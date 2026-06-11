using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed partial class EventHandlerTraceModeTests
{
    private static bool WaitForTraceCleanup(EventAnalyzer analyzer, Button button, string elementId, TimeSpan timeout)
    {
        return button.Dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var tracePayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
                var handlerPayload = JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"));
                if (!tracePayload.GetProperty("isTracing").GetBoolean()
                    && handlerPayload.GetProperty("handlerCount").GetInt32() == 0)
                {
                    return true;
                }

                var frame = new DispatcherFrame();
                button.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }

            return false;
        });
    }

    private static bool WaitForDeferredCleanupCompletedTrace(EventAnalyzer analyzer, Button button, TimeSpan timeout)
    {
        return button.Dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var tracePayload = JsonSerializer.SerializeToElement(analyzer.GetEventTrace());
                if (!tracePayload.GetProperty("isTracing").GetBoolean()
                    && tracePayload.TryGetProperty("cleanupState", out var cleanupState)
                    && string.Equals(cleanupState.GetString(), "deferredCompleted", StringComparison.Ordinal))
                {
                    return true;
                }

                var frame = new DispatcherFrame();
                button.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }

            return false;
        });
    }

    private static bool WaitForTaskCompletion(Task task, Dispatcher dispatcher, TimeSpan timeout)
    {
        return dispatcher.Invoke(() =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }

            return task.IsCompleted;
        });
    }
}
