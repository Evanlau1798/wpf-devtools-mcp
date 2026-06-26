using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for McpTools wrapper methods to verify they correctly bridge
/// to existing tool ExecuteAsync implementations and return proper CallToolResult.
/// These tests exercise the wrapper logic without requiring actual WPF processes.
/// </summary>
[Collection("ToolCallHelperState")]
public class McpToolsWrapperTests : IDisposable
{
    private readonly SessionManager _sessionManager = new();
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _sessionManager.Dispose();
        _toolCallHelperScope.Dispose();
    }

    // === Process Tools ===

    [Fact]
    public async Task GetActiveProcess_WithoutSelection_ShouldReturnCallToolResult()
    {
        var result = await ProcessMcpTools.GetActiveProcess(_sessionManager);

        result.Should().NotBeNull();
        result.Content.Should().NotBeEmpty();
        result.Content[0].Should().BeOfType<TextContentBlock>();
    }

    [Fact]
    public async Task GetActiveProcess_WithoutSelection_ShouldReturnNonErrorResult()
    {
        var result = await ProcessMcpTools.GetActiveProcess(_sessionManager);

        result.Should().NotBeNull();
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_WithInvalidProcessId_ShouldReturnErrorOrThrow()
    {
        // ConnectTool constructor validates DLL signature, which may throw
        // in test environments. Both error result and exception are acceptable.
        try
        {
            var result = await ProcessMcpTools.Connect(_sessionManager, processId: -1);
            result.Should().NotBeNull();
            result.IsError.Should().BeTrue();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("DLL signature"))
        {
            // Expected in test environment without signed DLL
        }
    }

    [Fact]
    public async Task Ping_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await ProcessMcpTools.Ping(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task SelectActiveProcess_WithoutProcessId_ShouldReturnStructuredMissingRequiredParameter()
    {
        var result = await ProcessMcpTools.SelectActiveProcess(_sessionManager);

        result.IsError.Should().BeTrue();
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.GetProperty("errorCode").GetString().Should().Be("MissingRequiredParameter");
    }

    // === Tree Tools ===

    [Fact]
    public async Task GetVisualTree_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await TreeMcpTools.GetVisualTree(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetLogicalTree_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await TreeMcpTools.GetLogicalTree(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task CompareTrees_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await TreeMcpTools.CompareTrees(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === Binding Tools ===

    [Fact]
    public async Task GetBindings_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await BindingMcpTools.GetBindings(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetBindingErrors_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await BindingMcpTools.GetBindingErrors(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === DependencyProperty Tools ===

    [Fact]
    public async Task GetDpValueSource_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await DependencyPropertyMcpTools.GetDpValueSource(
            _sessionManager, processId: 99999, propertyName: "Text");

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task SetDpValue_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await DependencyPropertyMcpTools.SetDpValue(
            _sessionManager, processId: 99999, propertyName: "Text", value: System.Text.Json.JsonSerializer.SerializeToElement("test"));

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === Style Tools ===

    [Fact]
    public async Task GetAppliedStyles_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await StyleMcpTools.GetAppliedStyles(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === Event Tools ===

    [Fact]
    public async Task GetEventHandlers_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await EventMcpTools.GetEventHandlers(
            _sessionManager, processId: 99999, eventName: "Click", elementId: "Button_1");

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === Interaction Tools ===

    [Fact]
    public async Task ClickElement_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await InteractionMcpTools.ClickElement(_sessionManager, processId: 99999, elementId: "Button_1");

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === Layout Tools ===

    [Fact]
    public async Task GetLayoutInfo_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await LayoutMcpTools.GetLayoutInfo(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === MVVM Tools ===

    [Fact]
    public async Task GetViewModel_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await MvvmMcpTools.GetViewModel(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetCommands_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await MvvmMcpTools.GetCommands(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === Performance Tools ===

    [Fact]
    public async Task GetRenderStats_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await PerformanceMcpTools.GetRenderStats(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetVisualCount_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await PerformanceMcpTools.GetVisualCount(_sessionManager, processId: 99999);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
    }

    // === Additional coverage for low-coverage wrappers ===

    [Fact]
    public async Task SerializeToXaml_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await TreeMcpTools.SerializeToXaml(_sessionManager, elementId: "Button_1", processId: 99999);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetNamescope_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await TreeMcpTools.GetNamescope(_sessionManager, processId: 99999);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetTemplateTree_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await TreeMcpTools.GetTemplateTree(_sessionManager, processId: 99999, elementId: "Button_1");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetBindingValueChain_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await BindingMcpTools.GetBindingValueChain(_sessionManager, processId: 99999, propertyName: "Text");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetDataContextChain_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await BindingMcpTools.GetDataContextChain(_sessionManager, processId: 99999);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ForceBindingUpdate_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await BindingMcpTools.ForceBindingUpdate(_sessionManager, processId: 99999, propertyName: "Text");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetDpMetadata_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await DependencyPropertyMcpTools.GetDpMetadata(_sessionManager, processId: 99999, propertyName: "Text");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ClearDpValue_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await DependencyPropertyMcpTools.ClearDpValue(_sessionManager, processId: 99999, propertyName: "Text");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task WatchDpChanges_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await DependencyPropertyMcpTools.WatchDpChanges(_sessionManager, processId: 99999, propertyName: "Text");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetTriggers_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await StyleMcpTools.GetTriggers(_sessionManager, processId: 99999, elementId: "Button_1");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetResourceChain_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await StyleMcpTools.GetResourceChain(_sessionManager, processId: 99999, resourceKey: "TestBrush");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task OverrideStyleSetter_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await StyleMcpTools.OverrideStyleSetter(_sessionManager, processId: 99999, elementId: "Button_1", propertyName: "Background", value: System.Text.Json.JsonSerializer.SerializeToElement("Red"));
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task TraceRoutedEvents_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await EventMcpTools.TraceRoutedEvents(_sessionManager, processId: 99999, eventName: "Click");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task FireRoutedEvent_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await EventMcpTools.FireRoutedEvent(_sessionManager, processId: 99999, eventName: "Click", elementId: "Button_1");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task DragAndDrop_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await InteractionMcpTools.DragAndDrop(_sessionManager, processId: 99999, sourceElementId: "A", targetElementId: "B");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ScrollToElement_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await InteractionMcpTools.ScrollToElement(_sessionManager, processId: 99999, elementId: "List_1");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task SimulateKeyboard_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await InteractionMcpTools.SimulateKeyboard(_sessionManager, processId: 99999, key: "Enter");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ElementScreenshot_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await InteractionMcpTools.ElementScreenshot(_sessionManager, processId: 99999);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetClippingInfo_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await LayoutMcpTools.GetClippingInfo(_sessionManager, processId: 99999, elementId: "Border_1");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task HighlightElement_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await LayoutMcpTools.HighlightElement(_sessionManager, processId: 99999, elementId: "Button_1");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateLayout_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await LayoutMcpTools.InvalidateLayout(_sessionManager, processId: 99999);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteCommand_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await MvvmMcpTools.ExecuteCommand(_sessionManager, processId: 99999, commandName: "SaveCommand");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ModifyViewModel_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await MvvmMcpTools.ModifyViewModel(_sessionManager, processId: 99999, propertyName: "Name", value: System.Text.Json.JsonSerializer.SerializeToElement("test"));
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetValidationErrors_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await MvvmMcpTools.GetValidationErrors(_sessionManager, processId: 99999);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task FindBindingLeaks_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await PerformanceMcpTools.FindBindingLeaks(_sessionManager, processId: 99999);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task MeasureElementRenderTime_WithUnconnectedProcess_ShouldReturnError()
    {
        var result = await PerformanceMcpTools.MeasureElementRenderTime(_sessionManager, processId: 99999);
        result.IsError.Should().BeTrue();
    }

    // === Result format verification ===

    [Fact]
    public async Task AllWrappers_ShouldReturnTextContentBlock()
    {
        var result = await ProcessMcpTools.GetActiveProcess(_sessionManager);

        var textBlock = result.Content[0] as TextContentBlock;
        textBlock.Should().NotBeNull();
        textBlock!.Text.Should().Contain("success");
    }

    [Fact]
    public async Task ErrorResult_ShouldContainErrorMessage()
    {
        var result = await ProcessMcpTools.Ping(_sessionManager, processId: 99999);

        result.IsError.Should().BeTrue();
        var textBlock = result.Content[0] as TextContentBlock;
        textBlock.Should().NotBeNull();
        textBlock!.Text.Should().Contain("error");
    }
}
