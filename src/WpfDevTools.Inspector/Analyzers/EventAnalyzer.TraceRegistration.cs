using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class EventAnalyzer
{
    private RoutedEventHandler CreateTraceHandler(string tracedElementId, string traceSessionId)
    {
        return (sender, e) =>
        {
            lock (_lock)
            {
                if (_isTraceAcceptingEvents
                    && string.Equals(_activeTraceSession?.Metadata.SessionId, traceSessionId, StringComparison.Ordinal))
                {
                    _handlerInvocationCount++;
                    var senderType = sender?.GetType().Name;
                    var senderName = (sender as FrameworkElement)?.Name;
                    var routingStrategy = e.RoutedEvent.RoutingStrategy.ToString();
                    var originalSourceType = (e.OriginalSource as FrameworkElement)?.GetType().Name;

                    _eventTrace.Add(new
                    {
                        timestamp = DateTime.UtcNow,
                        sender = senderType,
                        senderName,
                        eventName = e.RoutedEvent.Name,
                        routingStrategy,
                        handled = e.Handled,
                        originalSource = originalSourceType
                    });
                    EnqueueRoutedEventRecord(
                        tracedElementId,
                        sender as UIElement ?? (e.OriginalSource as UIElement) ?? throw new InvalidOperationException("Routed event trace expected a UIElement sender or original source."),
                        e.RoutedEvent,
                        e.Handled,
                        e.OriginalSource,
                        senderType,
                        senderName,
                        routingStrategy,
                        originalSourceType,
                        $"event:{traceSessionId}:{tracedElementId}:{e.RoutedEvent.Name}:{_handlerInvocationCount}");
                }
            }
        };
    }

    private static void RegisterTraceHandlers(
        UIElement targetElement,
        RoutedEvent routedEvent,
        RoutedEventHandler handler,
        string eventName,
        List<HandlerRegistration> registrations)
    {
        try
        {
            // 1. Register on the target element
            targetElement.AddHandler(routedEvent, handler, handledEventsToo: true);
            registrations.Add(new HandlerRegistration(targetElement, routedEvent, handler));

            // 2. Register on root window for bubble/tunnel capture
            try
            {
                var rootWindow = Window.GetWindow(targetElement);
                if (rootWindow != null && !ReferenceEquals(rootWindow, targetElement))
                {
                    rootWindow.AddHandler(routedEvent, handler, handledEventsToo: true);
                    registrations.Add(new HandlerRegistration(rootWindow, routedEvent, handler));
                }
            }
            catch (InvalidOperationException)
            {
                // Window.GetWindow may fail with cross-thread access; safe to skip
            }

            // 3. Try to find and register the Preview (tunneling) variant
            var previewEvent = FindPreviewRoutedEvent(targetElement, eventName);
            if (previewEvent != null)
            {
                targetElement.AddHandler(previewEvent, handler, handledEventsToo: true);
                registrations.Add(new HandlerRegistration(targetElement, previewEvent, handler));
            }
        }
        catch
        {
            TryRollbackPartialRegistrations(registrations);
            throw;
        }
    }

    private static RoutedEvent? FindPreviewRoutedEvent(UIElement element, string eventName)
    {
        if (eventName.StartsWith("Preview", StringComparison.Ordinal))
        {
            return null;
        }

        return RoutedEventDiscovery.FindRoutedEvent(element.GetType(), "Preview" + eventName);
    }

    private static void TryRollbackPartialRegistrations(List<HandlerRegistration> registrations)
    {
        if (registrations.Count == 0)
        {
            return;
        }

        try
        {
            RemoveAllHandlers(registrations);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"EventAnalyzer failed to rollback partial routed-event trace registrations: {SensitiveLogRedactor.Redact(ex.ToString())}");
        }
    }

    private void ScheduleAutoStop(TraceSessionHandle sessionHandle, int cappedDuration)
    {
        Task.Delay(cappedDuration, sessionHandle.TokenSource.Token).ContinueWith(completedDelay =>
        {
            CleanupTraceSessionCore(sessionHandle, out _, treatDeferredCleanupAsSuccess: false);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private RoutedEvent? FindRoutedEvent(UIElement element, string eventName)
    {
        return RoutedEventDiscovery.FindRoutedEvent(element.GetType(), eventName);
    }

    private static bool IsButtonClickEvent(UIElement element, string eventName)
    {
        return element is ButtonBase
            && string.Equals(eventName, "Click", StringComparison.OrdinalIgnoreCase);
    }

    private static RoutedEventArgs CreateRoutedEventArgs(RoutedEvent routedEvent, UIElement sourceElement)
    {
        var eventArgsType = routedEvent.HandlerType?.GetMethod("Invoke")?.GetParameters().LastOrDefault()?.ParameterType;

        if (eventArgsType == typeof(MouseButtonEventArgs))
        {
            return new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = routedEvent,
                Source = sourceElement
            };
        }

        if (eventArgsType == typeof(MouseEventArgs))
        {
            return new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = routedEvent,
                Source = sourceElement
            };
        }

        return new RoutedEventArgs(routedEvent, sourceElement);
    }

    private void EnqueueRoutedEventRecord(
        string elementId,
        UIElement element,
        RoutedEvent routedEvent,
        bool? handled,
        object? originalSource,
        string? senderType = null,
        string? senderName = null,
        string? routingStrategy = null,
        string? originalSourceType = null,
        string? sourceKey = null)
    {
        _watchEventBuffer?.Enqueue(new WatchEventRecord(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: sourceKey ?? $"tool:routed:{elementId}:{routedEvent.Name}:{Guid.NewGuid():N}",
            ElementId: elementId,
            PropertyName: null,
            EventName: routedEvent.Name,
            NewValue: null,
            ValueType: null,
            SenderType: senderType ?? element.GetType().Name,
            SenderName: senderName ?? (element as FrameworkElement)?.Name,
            RoutingStrategy: routingStrategy ?? routedEvent.RoutingStrategy.ToString(),
            Handled: handled,
            OriginalSourceType: originalSourceType ?? originalSource?.GetType().Name));
    }

    private static object CreatePendingEventContract(WatchEventRecord record) => new
    {
        eventType = record.EventType,
        timestampUtc = record.TimestampUtc,
        sourceKey = record.SourceKey,
        elementId = record.ElementId,
        propertyName = record.PropertyName,
        eventName = record.EventName,
        newValue = record.NewValue,
        valueType = record.ValueType,
        senderType = record.SenderType,
        senderName = record.SenderName,
        routingStrategy = record.RoutingStrategy,
        handled = record.Handled,
        originalSourceType = record.OriginalSourceType
    };
}
