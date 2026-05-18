using System.Reflection;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector;

public sealed class RequestDispatcherRegistryTests
{
    private static readonly (Type HandlerType, string[] Methods)[] ExpectedHandlerGroups =
    [
        (typeof(TreeHandlers), ["get_visual_tree", "get_logical_tree", "compare_trees", "serialize_to_xaml", "get_namescope", "get_template_tree", "get_windows"]),
        (typeof(ElementSearchHandlers), ["find_elements"]),
        (typeof(BindingHandlers), ["get_bindings", "get_affected_elements", "get_binding_mismatches", "get_binding_errors", "get_datacontext_chain", "get_binding_value_chain", "force_binding_update"]),
        (typeof(MvvmHandlers), ["get_viewmodel", "get_commands", "execute_command", "modify_viewmodel", "get_validation_errors"]),
        (typeof(DependencyPropertyHandlers), ["get_dp_value_source", "get_dp_metadata", "set_dp_value", "clear_dp_value", "capture_dp_expression_restore", "restore_dp_expression", "watch_dp_changes", "wait_for_dp_change"]),
        (typeof(LayoutHandlers), ["get_layout_info", "get_clipping_info", "diagnose_visibility", "highlight_element", "invalidate_layout"]),
        (typeof(InteractionHandlers), ["click_element", "get_interaction_readiness", "get_focus_state", "focus_element", "scroll_to_element", "element_screenshot", "drag_and_drop", "simulate_keyboard"]),
        (typeof(StyleHandlers), ["get_applied_styles", "get_triggers", "get_resource_chain", "override_style_setter"]),
        (typeof(EventHandlers), ["trace_routed_events", "get_event_handlers", "fire_routed_event", "drain_events"]),
        (typeof(PerformanceHandlers), ["get_render_stats", "find_binding_leaks", "measure_element_render_time", "get_visual_count"]),
        (typeof(SceneSummaryHandlers), ["get_ui_summary", "get_form_summary"]),
        (typeof(ElementSnapshotHandlers), ["get_element_snapshot"])
    ];

    [Fact]
    public void Create_ShouldComposeSharedHandlersAndRegisterSupportedMethods()
    {
        using var logger = new FileLogger();

        var composition = RequestDispatcherRegistry.Create(logger, eventTraceCleanupInvoker: null);
        try
        {
            var expectedMethods = ExpectedHandlerGroups
                .SelectMany(group => group.Methods)
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToArray();
            var actualMethods = composition.HandlerMap.Keys
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToArray();

            composition.ElementFinder.Should().NotBeNull();
            composition.EventAnalyzer.Should().NotBeNull();
            actualMethods.Should().Equal(expectedMethods,
                "the registry should remain the single parity-checked composition contract for every dispatcher-exposed handler method");

            foreach (var (handlerType, methods) in ExpectedHandlerGroups)
            {
                var groupHandler = composition.HandlerMap[methods[0]];

                groupHandler.Should().BeOfType(handlerType);
                foreach (var method in methods.Skip(1))
                {
                    composition.HandlerMap[method].Should().BeSameAs(groupHandler,
                        "methods exported by the same handler should stay grouped on the same IRequestHandler instance after registry refactors");
                }
            }

            var eventAnalyzer = (EventAnalyzer)typeof(EventHandlers)
                .GetField("_eventAnalyzer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(composition.HandlerMap["trace_routed_events"])!;
            var bindingAnalyzer = (BindingAnalyzer)typeof(BindingHandlers)
                .GetField("_bindingAnalyzer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(composition.HandlerMap["get_bindings"])!;

            eventAnalyzer.Should().BeSameAs(composition.EventAnalyzer);
            composition.OwnedDisposables.Should().Equal(
                [composition.EventAnalyzer, bindingAnalyzer, composition.ElementFinder],
                "the dispatcher lifecycle must release every disposable analyzer that owns process-global or timer state");
        }
        finally
        {
            foreach (var disposable in composition.OwnedDisposables)
            {
                disposable.Dispose();
            }
        }
    }

    [Fact]
    public void CreateHandlerMap_WithDuplicateMethodRegistrations_ShouldFailFast()
    {
        var firstHandler = new DuplicateMethodHandler();
        var secondHandler = new DuplicateMethodHandler();

        Action act = () => RequestDispatcherRegistry.CreateHandlerMap([firstHandler, secondHandler]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate request handler registration for method 'duplicate_method'*",
                "registry extraction should fail closed instead of silently changing dispatcher behavior when two handlers export the same method name");
    }

    private sealed class DuplicateMethodHandler : IRequestHandler
    {
        public IEnumerable<string> GetSupportedMethods() => ["duplicate_method"];

        public Task<object> HandleAsync(string method, System.Text.Json.JsonElement? @params, CancellationToken cancellationToken)
            => Task.FromResult<object>(new { success = true });
    }
}
