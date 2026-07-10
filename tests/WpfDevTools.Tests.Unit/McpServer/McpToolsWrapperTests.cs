using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class McpToolsWrapperTests : IDisposable
{
    private const int UnconnectedProcessId = 99999;
    private readonly SessionManager _sessionManager = new();
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _sessionManager.Dispose();
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task GetActiveProcess_WithoutSelection_ShouldReturnNonErrorTextResult()
    {
        var result = await ProcessMcpTools.GetActiveProcess(_sessionManager);

        result.Should().NotBeNull();
        result.IsError.Should().BeFalse();
        result.Content.Should().NotBeEmpty();
        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        textBlock.Text.Should().Contain("success");
    }

    [Fact]
    public async Task Connect_WithInvalidProcessId_ShouldReturnErrorOrThrow()
    {
        try
        {
            var result = await ProcessMcpTools.Connect(_sessionManager, processId: -1);
            result.Should().NotBeNull();
            result.IsError.Should().BeTrue();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("DLL signature"))
        {
            // Unsigned development payloads fail before the invalid process is evaluated.
        }
    }

    [Fact]
    public async Task SelectActiveProcess_WithoutProcessId_ShouldReturnStructuredMissingRequiredParameter()
    {
        var result = await ProcessMcpTools.SelectActiveProcess(_sessionManager);

        result.IsError.Should().BeTrue();
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.GetProperty("errorCode").GetString().Should().Be("MissingRequiredParameter");
    }

    [Fact]
    public async Task ProcessBoundWrappers_WithUnconnectedProcess_ShouldReturnNamedErrorResults()
    {
        var stringValue = JsonSerializer.SerializeToElement("test");
        var operations = new (string Name, Func<Task<CallToolResult>> Execute)[]
        {
            ("ping", () => ProcessMcpTools.Ping(_sessionManager, processId: UnconnectedProcessId)),
            ("get_visual_tree", () => TreeMcpTools.GetVisualTree(_sessionManager, processId: UnconnectedProcessId)),
            ("get_logical_tree", () => TreeMcpTools.GetLogicalTree(_sessionManager, processId: UnconnectedProcessId)),
            ("compare_trees", () => TreeMcpTools.CompareTrees(_sessionManager, processId: UnconnectedProcessId)),
            ("serialize_to_xaml", () => TreeMcpTools.SerializeToXaml(_sessionManager, elementId: "Button_1", processId: UnconnectedProcessId)),
            ("get_namescope", () => TreeMcpTools.GetNamescope(_sessionManager, processId: UnconnectedProcessId)),
            ("get_template_tree", () => TreeMcpTools.GetTemplateTree(_sessionManager, processId: UnconnectedProcessId, elementId: "Button_1")),
            ("get_bindings", () => BindingMcpTools.GetBindings(_sessionManager, processId: UnconnectedProcessId)),
            ("get_binding_errors", () => BindingMcpTools.GetBindingErrors(_sessionManager, processId: UnconnectedProcessId)),
            ("get_binding_value_chain", () => BindingMcpTools.GetBindingValueChain(_sessionManager, processId: UnconnectedProcessId, propertyName: "Text")),
            ("get_datacontext_chain", () => BindingMcpTools.GetDataContextChain(_sessionManager, processId: UnconnectedProcessId)),
            ("force_binding_update", () => BindingMcpTools.ForceBindingUpdate(_sessionManager, processId: UnconnectedProcessId, propertyName: "Text")),
            ("get_dp_value_source", () => DependencyPropertyMcpTools.GetDpValueSource(_sessionManager, processId: UnconnectedProcessId, propertyName: "Text")),
            ("set_dp_value", () => DependencyPropertyMcpTools.SetDpValue(_sessionManager, processId: UnconnectedProcessId, propertyName: "Text", value: stringValue)),
            ("get_dp_metadata", () => DependencyPropertyMcpTools.GetDpMetadata(_sessionManager, processId: UnconnectedProcessId, propertyName: "Text")),
            ("clear_dp_value", () => DependencyPropertyMcpTools.ClearDpValue(_sessionManager, processId: UnconnectedProcessId, propertyName: "Text")),
            ("watch_dp_changes", () => DependencyPropertyMcpTools.WatchDpChanges(_sessionManager, processId: UnconnectedProcessId, propertyName: "Text")),
            ("get_applied_styles", () => StyleMcpTools.GetAppliedStyles(_sessionManager, processId: UnconnectedProcessId)),
            ("get_triggers", () => StyleMcpTools.GetTriggers(_sessionManager, processId: UnconnectedProcessId, elementId: "Button_1")),
            ("get_resource_chain", () => StyleMcpTools.GetResourceChain(_sessionManager, processId: UnconnectedProcessId, resourceKey: "TestBrush")),
            ("override_style_setter", () => StyleMcpTools.OverrideStyleSetter(_sessionManager, processId: UnconnectedProcessId, elementId: "Button_1", propertyName: "Background", value: stringValue)),
            ("get_event_handlers", () => EventMcpTools.GetEventHandlers(_sessionManager, processId: UnconnectedProcessId, eventName: "Click", elementId: "Button_1")),
            ("trace_routed_events", () => EventMcpTools.TraceRoutedEvents(_sessionManager, UnconnectedProcessId, eventName: "Click")),
            ("fire_routed_event", () => EventMcpTools.FireRoutedEvent(_sessionManager, processId: UnconnectedProcessId, eventName: "Click", elementId: "Button_1")),
            ("click_element", () => InteractionMcpTools.ClickElement(_sessionManager, processId: UnconnectedProcessId, elementId: "Button_1")),
            ("drag_and_drop", () => InteractionMcpTools.DragAndDrop(_sessionManager, processId: UnconnectedProcessId, sourceElementId: "A", targetElementId: "B")),
            ("scroll_to_element", () => InteractionMcpTools.ScrollToElement(_sessionManager, processId: UnconnectedProcessId, elementId: "List_1")),
            ("simulate_keyboard", () => InteractionMcpTools.SimulateKeyboard(_sessionManager, processId: UnconnectedProcessId, key: "Enter")),
            ("element_screenshot", () => InteractionMcpTools.ElementScreenshot(_sessionManager, processId: UnconnectedProcessId)),
            ("get_layout_info", () => LayoutMcpTools.GetLayoutInfo(_sessionManager, processId: UnconnectedProcessId)),
            ("get_clipping_info", () => LayoutMcpTools.GetClippingInfo(_sessionManager, processId: UnconnectedProcessId, elementId: "Border_1")),
            ("highlight_element", () => LayoutMcpTools.HighlightElement(_sessionManager, processId: UnconnectedProcessId, elementId: "Button_1")),
            ("invalidate_layout", () => LayoutMcpTools.InvalidateLayout(_sessionManager, processId: UnconnectedProcessId)),
            ("get_viewmodel", () => MvvmMcpTools.GetViewModel(_sessionManager, processId: UnconnectedProcessId)),
            ("get_commands", () => MvvmMcpTools.GetCommands(_sessionManager, processId: UnconnectedProcessId)),
            ("execute_command", () => MvvmMcpTools.ExecuteCommand(_sessionManager, processId: UnconnectedProcessId, commandName: "SaveCommand")),
            ("modify_viewmodel", () => MvvmMcpTools.ModifyViewModel(_sessionManager, processId: UnconnectedProcessId, propertyName: "Name", value: stringValue)),
            ("get_validation_errors", () => MvvmMcpTools.GetValidationErrors(_sessionManager, processId: UnconnectedProcessId)),
            ("get_render_stats", () => PerformanceMcpTools.GetRenderStats(_sessionManager, processId: UnconnectedProcessId)),
            ("get_visual_count", () => PerformanceMcpTools.GetVisualCount(_sessionManager, processId: UnconnectedProcessId)),
            ("find_binding_leaks", () => PerformanceMcpTools.FindBindingLeaks(_sessionManager, processId: UnconnectedProcessId)),
            ("measure_element_render_time", () => PerformanceMcpTools.MeasureElementRenderTime(_sessionManager, processId: UnconnectedProcessId))
        };

        operations.Should().HaveCount(42);
        operations.Select(operation => operation.Name).Should().OnlyHaveUniqueItems();

        foreach (var operation in operations)
        {
            var result = await operation.Execute();
            using var scope = new AssertionScope(operation.Name);
            result.Should().NotBeNull();
            result.IsError.Should().BeTrue();
            result.Content.Should().NotBeEmpty();
            var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
            textBlock.Text.Should().Contain("error");
        }
    }
}
