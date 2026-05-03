using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolRequiredElementIdTests
{
    [Theory]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools.GetEventHandlers))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools.FireRoutedEvent))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.StyleMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.StyleMcpTools.OverrideStyleSetter))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.LayoutMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.LayoutMcpTools.HighlightElement))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.LayoutMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.LayoutMcpTools.GetClippingInfo))]
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

    private static JsonElement CreateInputSchema(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema tests do not invoke tools."))
            .BuildServiceProvider();

        return McpServerTool.Create(method!, target: null, new McpServerToolCreateOptions { Services = services })
            .ProtocolTool
            .InputSchema;
    }

    private static JsonElement ToJsonElement(object value) =>
        JsonSerializer.SerializeToElement(value);
}
