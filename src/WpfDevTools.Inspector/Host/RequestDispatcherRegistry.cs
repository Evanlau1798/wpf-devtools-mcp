using System.Windows.Threading;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Host;

internal sealed class RequestDispatcherComposition
{
    public RequestDispatcherComposition(
        ElementFinder elementFinder,
        EventAnalyzer eventAnalyzer,
        IReadOnlyDictionary<string, IRequestHandler> handlerMap)
    {
        ElementFinder = elementFinder;
        EventAnalyzer = eventAnalyzer;
        HandlerMap = handlerMap;
    }

    public ElementFinder ElementFinder { get; }

    public EventAnalyzer EventAnalyzer { get; }

    public IReadOnlyDictionary<string, IRequestHandler> HandlerMap { get; }
}

internal static class RequestDispatcherRegistry
{
    public static RequestDispatcherComposition Create(
        FileLogger logger,
        Func<Dispatcher?, Action, Exception?>? eventTraceCleanupInvoker)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var elementFinder = new ElementFinder();
        var xamlSerializer = new XamlSerializer();
        var watchEventBuffer = new WatchEventBuffer();

        var visualTreeAnalyzer = new VisualTreeAnalyzer(elementFinder);
        var bindingAnalyzer = new BindingAnalyzer(elementFinder, watchEventBuffer);
        var logicalTreeAnalyzer = new LogicalTreeAnalyzer(elementFinder);
        var elementSearchAnalyzer = new ElementSearchAnalyzer(elementFinder);
        var mvvmAnalyzer = new MvvmAnalyzer(elementFinder, watchEventBuffer);
        var dependencyPropertyAnalyzer = new DependencyPropertyAnalyzer(elementFinder, watchEventBuffer);
        var layoutAnalyzer = new LayoutAnalyzer(elementFinder);
        var interactionAnalyzer = new InteractionAnalyzer(elementFinder, watchEventBuffer);
        var styleAnalyzer = new StyleAnalyzer(elementFinder);
        var eventAnalyzer = new EventAnalyzer(elementFinder, watchEventBuffer, eventTraceCleanupInvoker);
        var performanceAnalyzer = new PerformanceAnalyzer(elementFinder);
        var uiSummaryAnalyzer = new UiSummaryAnalyzer(elementFinder);
        var formSummaryAnalyzer = new FormSummaryAnalyzer(elementFinder);

        var treeHandlers = new TreeHandlers(visualTreeAnalyzer, logicalTreeAnalyzer, xamlSerializer, elementFinder);
        var elementSearchHandlers = new ElementSearchHandlers(elementSearchAnalyzer);
        var bindingHandlers = new BindingHandlers(bindingAnalyzer, elementFinder);
        var mvvmHandlers = new MvvmHandlers(mvvmAnalyzer);
        var dependencyPropertyHandlers = new DependencyPropertyHandlers(dependencyPropertyAnalyzer);
        var layoutHandlers = new LayoutHandlers(layoutAnalyzer);
        var interactionHandlers = new InteractionHandlers(interactionAnalyzer);
        var styleHandlers = new StyleHandlers(styleAnalyzer);
        var eventHandlers = new EventHandlers(eventAnalyzer, dependencyPropertyAnalyzer.ClearTransientWatchers);
        var performanceHandlers = new PerformanceHandlers(performanceAnalyzer);
        var sceneSummaryHandlers = new SceneSummaryHandlers(uiSummaryAnalyzer, formSummaryAnalyzer);
        var elementSnapshotHandlers = new ElementSnapshotHandlers(
            treeHandlers,
            bindingHandlers,
            mvvmHandlers,
            styleHandlers,
            layoutHandlers,
            dependencyPropertyHandlers);

        IRequestHandler[] handlers =
        [
            treeHandlers,
            elementSearchHandlers,
            bindingHandlers,
            mvvmHandlers,
            dependencyPropertyHandlers,
            layoutHandlers,
            interactionHandlers,
            styleHandlers,
            eventHandlers,
            performanceHandlers,
            sceneSummaryHandlers,
            elementSnapshotHandlers
        ];

        return new RequestDispatcherComposition(
            elementFinder,
            eventAnalyzer,
            CreateHandlerMap(handlers));
    }

    internal static IReadOnlyDictionary<string, IRequestHandler> CreateHandlerMap(IEnumerable<IRequestHandler> handlers)
    {
        var handlerMap = new Dictionary<string, IRequestHandler>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            foreach (var method in handler.GetSupportedMethods())
            {
                if (!handlerMap.TryAdd(method, handler))
                {
                    throw new InvalidOperationException(
                        $"Duplicate request handler registration for method '{method}'.");
                }
            }
        }

        return handlerMap;
    }
}