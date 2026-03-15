using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public class HandlerRoutingTests
{
    // ---- BindingHandlers ----

    [Fact]
    public void BindingHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(7);
        methods.Should().Contain("get_bindings");
        methods.Should().Contain("get_affected_elements");
        methods.Should().Contain("get_binding_mismatches");
        methods.Should().Contain("get_binding_errors");
        methods.Should().Contain("get_datacontext_chain");
        methods.Should().Contain("get_binding_value_chain");
        methods.Should().Contain("force_binding_update");
    }

    [Fact]
    public async Task BindingHandlers_HandleAsync_GetBindings_ShouldReturnResult()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());
        var result = await handler.HandleAsync("get_bindings", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BindingHandlers_HandleAsync_GetBindingErrors_ShouldReturnResult()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());
        var result = await handler.HandleAsync("get_binding_errors", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BindingHandlers_HandleAsync_GetDataContextChain_ShouldReturnResult()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());
        var result = await handler.HandleAsync("get_datacontext_chain", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ---- TreeHandlers ----

    [Fact]
    public void TreeHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(7);
        methods.Should().Contain("get_visual_tree");
        methods.Should().Contain("get_logical_tree");
        methods.Should().Contain("compare_trees");
        methods.Should().Contain("serialize_to_xaml");
        methods.Should().Contain("get_namescope");
        methods.Should().Contain("get_template_tree");
        methods.Should().Contain("get_windows");
    }

    [Fact]
    public async Task TreeHandlers_HandleAsync_GetVisualTree_ShouldReturnResult()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var result = await handler.HandleAsync("get_visual_tree", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task TreeHandlers_HandleAsync_GetLogicalTree_ShouldReturnResult()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var result = await handler.HandleAsync("get_logical_tree", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task TreeHandlers_HandleAsync_CompareTrees_ShouldReturnResult()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var result = await handler.HandleAsync("compare_trees", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task TreeHandlers_HandleAsync_GetTemplateTree_ShouldReturnResult()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var result = await handler.HandleAsync("get_template_tree", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task TreeHandlers_HandleAsync_GetNameScope_ShouldReturnResult()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var result = await handler.HandleAsync("get_namescope", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [StaFact]
    public async Task TreeHandlers_HandleAsync_SerializeToXaml_MissingElement_ShouldReturnStructuredError()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var parameters = JsonDocument.Parse("{\"elementId\":\"missing-element\"}").RootElement;

        var result = await handler.HandleAsync("serialize_to_xaml", parameters, CancellationToken.None);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(result, result!.GetType())).RootElement;

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
        json.GetProperty("hint").GetString().Should().Contain("elementId");
    }

    [Fact]
    public void ElementSearchHandlers_GetSupportedMethods_ShouldReturnFindElements()
    {
        var handler = new ElementSearchHandlers(new ElementSearchAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().ContainSingle().Which.Should().Be("find_elements");
    }

    [Fact]
    public async Task ElementSearchHandlers_HandleAsync_FindElements_ShouldReturnResult()
    {
        var handler = new ElementSearchHandlers(new ElementSearchAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("find_elements", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ---- DependencyPropertyHandlers ----

    [Fact]
    public void DependencyPropertyHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(8);
        methods.Should().Contain("get_dp_value_source");
        methods.Should().Contain("get_dp_metadata");
        methods.Should().Contain("set_dp_value");
        methods.Should().Contain("clear_dp_value");
        methods.Should().Contain("capture_dp_expression_restore");
        methods.Should().Contain("restore_dp_expression");
        methods.Should().Contain("watch_dp_changes");
        methods.Should().Contain("wait_for_dp_change");
    }

    // ---- MvvmHandlers ----

    [Fact]
    public void MvvmHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(5);
        methods.Should().Contain("get_viewmodel");
        methods.Should().Contain("get_commands");
        methods.Should().Contain("execute_command");
        methods.Should().Contain("modify_viewmodel");
        methods.Should().Contain("get_validation_errors");
    }

    [Fact]
    public async Task MvvmHandlers_HandleAsync_GetViewModel_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_viewmodel", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MvvmHandlers_HandleAsync_GetCommands_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_commands", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MvvmHandlers_HandleAsync_GetValidationErrors_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_validation_errors", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ---- LayoutHandlers ----

    [Fact]
    public void LayoutHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new LayoutHandlers(new LayoutAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(5);
        methods.Should().Contain("get_layout_info");
        methods.Should().Contain("get_clipping_info");
        methods.Should().Contain("diagnose_visibility");
        methods.Should().Contain("highlight_element");
        methods.Should().Contain("invalidate_layout");
    }

    [Fact]
    public async Task LayoutHandlers_HandleAsync_GetLayoutInfo_ShouldReturnResult()
    {
        var handler = new LayoutHandlers(new LayoutAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_layout_info", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task LayoutHandlers_HandleAsync_GetClippingInfo_ShouldReturnResult()
    {
        var handler = new LayoutHandlers(new LayoutAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_clipping_info", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task LayoutHandlers_HandleAsync_DiagnoseVisibility_ShouldReturnResult()
    {
        var handler = new LayoutHandlers(new LayoutAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("diagnose_visibility", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task LayoutHandlers_HandleAsync_HighlightElement_ShouldReturnResult()
    {
        var handler = new LayoutHandlers(new LayoutAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("highlight_element", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task LayoutHandlers_HandleAsync_InvalidateLayout_ShouldReturnResult()
    {
        var handler = new LayoutHandlers(new LayoutAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("invalidate_layout", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ---- StyleHandlers ----

    [Fact]
    public void StyleHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(4);
        methods.Should().Contain("get_applied_styles");
        methods.Should().Contain("get_triggers");
        methods.Should().Contain("get_resource_chain");
        methods.Should().Contain("override_style_setter");
    }

    [Fact]
    public async Task StyleHandlers_HandleAsync_GetAppliedStyles_ShouldReturnResult()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_applied_styles", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task StyleHandlers_HandleAsync_GetTriggers_ShouldReturnResult()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_triggers", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ---- EventHandlers ----

    [Fact]
    public void EventHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(4);
        methods.Should().Contain("trace_routed_events");
        methods.Should().Contain("get_event_handlers");
        methods.Should().Contain("fire_routed_event");
        methods.Should().Contain("drain_events");
    }

    // ---- InteractionHandlers ----

    [Fact]
    public void InteractionHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(8);
        methods.Should().Contain("click_element");
        methods.Should().Contain("get_interaction_readiness");
        methods.Should().Contain("get_focus_state");
        methods.Should().Contain("focus_element");
        methods.Should().Contain("scroll_to_element");
        methods.Should().Contain("element_screenshot");
        methods.Should().Contain("drag_and_drop");
        methods.Should().Contain("simulate_keyboard");
    }

    [Fact]
    public async Task InteractionHandlers_HandleAsync_ClickElement_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("click_element", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InteractionHandlers_HandleAsync_GetInteractionReadiness_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_interaction_readiness", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [StaFact]
    public async Task InteractionHandlers_HandleAsync_GetFocusState_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_focus_state", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InteractionHandlers_HandleAsync_ScrollToElement_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("scroll_to_element", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InteractionHandlers_HandleAsync_ElementScreenshot_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("element_screenshot", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InteractionHandlers_HandleAsync_DragAndDrop_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("drag_and_drop", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ---- PerformanceHandlers ----

    [Fact]
    public void PerformanceHandlers_GetSupportedMethods_ShouldReturnExpectedMethods()
    {
        var handler = new PerformanceHandlers(new PerformanceAnalyzer(new ElementFinder()));
        var methods = handler.GetSupportedMethods().ToList();

        methods.Should().HaveCount(4);
        methods.Should().Contain("get_render_stats");
        methods.Should().Contain("find_binding_leaks");
        methods.Should().Contain("measure_element_render_time");
        methods.Should().Contain("get_visual_count");
    }

    [Fact]
    public async Task PerformanceHandlers_HandleAsync_GetRenderStats_ShouldReturnResult()
    {
        var handler = new PerformanceHandlers(new PerformanceAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_render_stats", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformanceHandlers_HandleAsync_FindBindingLeaks_ShouldReturnResult()
    {
        var handler = new PerformanceHandlers(new PerformanceAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("find_binding_leaks", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformanceHandlers_HandleAsync_MeasureElementRenderTime_ShouldReturnResult()
    {
        var handler = new PerformanceHandlers(new PerformanceAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("measure_element_render_time", null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformanceHandlers_HandleAsync_GetVisualCount_ShouldReturnResult()
    {
        var handler = new PerformanceHandlers(new PerformanceAnalyzer(new ElementFinder()));
        var result = await handler.HandleAsync("get_visual_count", null, CancellationToken.None);
        result.Should().NotBeNull();
    }
}
