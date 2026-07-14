using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolRequiredElementIdTests
{
    [Theory]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools.GetEventHandlers))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools.FireRoutedEvent))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.StyleMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.StyleMcpTools.OverrideStyleSetter))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.LayoutMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.LayoutMcpTools.HighlightElement))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools.SerializeToXaml))]
    public void ToolsThatRequireConcreteTargets_ShouldPublishElementIdAsRequired(Type toolType, string methodName)
    {
        var schema = CreateInputSchema(toolType, methodName);

        schema.TryGetProperty("required", out var required).Should().BeTrue(
            "target-specific tools should publish at least one required parameter");
        required.EnumerateArray()
            .Select(required => required.GetString())
            .Should().Contain("elementId",
                "target-specific tools should not advertise elementId as optional in tools/list inputSchema");
    }

    [Fact]
    public void GetEventHandlers_ShouldPublishEventNameAsRequired()
    {
        var schema = CreateInputSchema(typeof(EventMcpTools), nameof(EventMcpTools.GetEventHandlers));

        schema.TryGetProperty("required", out var required).Should().BeTrue();
        required.EnumerateArray()
            .Select(required => required.GetString())
            .Should().Contain("eventName",
                "get_event_handlers should advertise the WPF event name as required in tools/list inputSchema");
    }

    [Fact]
    public void GetEventHandlers_ShouldRejectMissingEventNameBeforeSdkBinding()
    {
        var result = McpToolArgumentValidator.Validate(
            "get_event_handlers",
            new Dictionary<string, JsonElement>
            {
                ["processId"] = JsonSerializer.SerializeToElement(12345),
                ["elementId"] = JsonSerializer.SerializeToElement("Button_1")
            });

        result.Should().NotBeNull(
            "the server call filter should return a structured missing-parameter error before MCP SDK method binding");
        AssertStructuredMissingParameter(result!, "eventName");
    }

    [Fact]
    public void FireRoutedEvent_ShouldRejectMissingEventNameBeforeSdkBinding()
    {
        var result = McpToolArgumentValidator.Validate(
            "fire_routed_event",
            new Dictionary<string, JsonElement>
            {
                ["processId"] = JsonSerializer.SerializeToElement(12345),
                ["elementId"] = JsonSerializer.SerializeToElement("Button_1")
            });

        result.Should().NotBeNull(
            "mutating event tools should return a structured missing-parameter error before MCP SDK method binding");
        AssertStructuredMissingParameter(result!, "eventName");
    }

    [Fact]
    public void GetEventHandlers_ShouldRejectMissingElementIdBeforeSdkBinding()
    {
        var result = McpToolArgumentValidator.Validate(
            "get_event_handlers",
            new Dictionary<string, JsonElement>
            {
                ["processId"] = JsonSerializer.SerializeToElement(12345),
                ["eventName"] = JsonSerializer.SerializeToElement("Click")
            });

        result.Should().NotBeNull(
            "target-specific event tools should return a structured missing-parameter error before MCP SDK method binding");
        AssertStructuredMissingParameter(result!, "elementId");
    }

    [Theory]
    [InlineData("get_state_diff", "snapshotId")]
    [InlineData("restore_state_snapshot", "snapshotId")]
    [InlineData("simulate_keyboard", "key")]
    [InlineData("execute_command", "commandName")]
    public void ToolsWithRequiredNonElementParameters_ShouldRejectMissingArgumentBeforeSdkBinding(
        string toolName,
        string parameterName)
    {
        var result = McpToolArgumentValidator.Validate(
            toolName,
            new Dictionary<string, JsonElement>
            {
                ["processId"] = JsonSerializer.SerializeToElement(12345)
            });

        result.Should().NotBeNull(
            "the server call filter should return a structured missing-parameter error before MCP SDK method binding");
        AssertStructuredMissingParameter(result!, parameterName);
    }

    private static void AssertStructuredMissingParameter(CallToolResult result, string parameterName)
    {
        result.IsError.Should().BeTrue();
        result.StructuredContent.Should().NotBeNull();
        var content = result.StructuredContent!.Value;
        content.GetProperty("errorCode").GetString().Should().Be("MissingRequiredParameter");
        content.GetProperty("error").GetString().Should().Contain(parameterName);
        content.GetProperty("hint").GetString().Should().Contain(parameterName);
    }

    [Theory]
    [MemberData(nameof(MissingElementIdToolCases))]
    public async Task ToolsThatRequireConcreteTargets_ShouldRejectMissingElementIdBeforePipeAccess(
        Func<SessionManager, Task<object>> executeTool)
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);

        var result = await executeTool(sessionManager);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("error").GetString().Should().Contain("Missing required parameter: elementId");
        payload.GetProperty("errorCode").GetString().Should().Be("MissingRequiredParameter");
    }

    public static TheoryData<Func<SessionManager, Task<object>>> MissingElementIdToolCases()
    {
        return new TheoryData<Func<SessionManager, Task<object>>>
        {
            sessionManager => new GetEventHandlersTool(sessionManager).ExecuteAsync(
                ToJsonElement(new { processId = 12345, eventName = "Click" }),
                CancellationToken.None),
            sessionManager => new FireRoutedEventTool(sessionManager).ExecuteAsync(
                ToJsonElement(new { processId = 12345, eventName = "Click" }),
                CancellationToken.None),
            sessionManager => new OverrideStyleSetterTool(sessionManager).ExecuteAsync(
                ToJsonElement(new { processId = 12345, propertyName = "Background", value = "Red" }),
                CancellationToken.None),
            sessionManager => new GenericPipeTool(
                sessionManager,
                "highlight_element",
                GenericPipeTool.ExtractHighlightElementParams).ExecuteAsync(
                    ToJsonElement(new { processId = 12345 }),
                    CancellationToken.None)
        };
    }

    private static JsonElement CreateInputSchema(Type toolType, string methodName) =>
        CreateTool(toolType, methodName).ProtocolTool.InputSchema;

    private static McpServerTool CreateTool(
        Type toolType,
        string methodName,
        IServiceProvider? services = null)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        return McpServerTool.Create(method!, target: null, new McpServerToolCreateOptions { Services = services });
    }

    private static JsonElement ToJsonElement(object value) =>
        JsonSerializer.SerializeToElement(value);
}
