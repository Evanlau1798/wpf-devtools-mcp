using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("ToolCallHelperState")]
public sealed class GetInteractionReadinessToolTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassThroughStructuredReadinessPayload()
    {
        const int processId = 52040;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"elementId":"Button_1","interactionType":"Click","isReady":false,"blockers":[{"reason":"CommandCannotExecute"}],"elementState":{"isEnabled":false}}""");

        var tool = new GetInteractionReadinessTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1"
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isReady").GetBoolean().Should().BeFalse();
        result.GetProperty("blockers").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithDisabledAndCommandBlockers_ShouldSuggestInspectionTools()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "Button_1",
                interactionType = "Click",
                isReady = false,
                blockers = new[]
                {
                    new { reason = "ElementDisabled" },
                    new { reason = "CommandCannotExecute" }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Button_1")),
            CancellationToken.None,
            toolName: "get_interaction_readiness");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(2);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_dp_value_source");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("Button_1");
        nextSteps[0].GetProperty("params").GetProperty("propertyName").GetString().Should().Be("IsEnabled");
        nextSteps[1].GetProperty("tool").GetString().Should().Be("get_commands");
        nextSteps[1].GetProperty("params").GetProperty("elementId").GetString().Should().Be("Button_1");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithVisibilityBlocker_ShouldSuggestDiagnoseVisibility()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "Button_2",
                interactionType = "Click",
                isReady = false,
                blockers = new[]
                {
                    new { reason = "VisibilityBlocked" }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Button_2")),
            CancellationToken.None,
            toolName: "get_interaction_readiness");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("diagnose_visibility");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("Button_2");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithInactiveTabBlockerAndActivationTarget_ShouldSuggestClickingTabItemFirst()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "Button_3",
                interactionType = "Click",
                isReady = false,
                blockers = new[]
                {
                    new { reason = "ElementInInactiveTab" }
                },
                activationPath = "MainTabControl -> DetailsTab",
                activationTarget = new
                {
                    tabItemElementId = "TabItem_Details",
                    tabItemName = "DetailsTab"
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Button_3")),
            CancellationToken.None,
            toolName: "get_interaction_readiness");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("click_element");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TabItem_Details");
    }

    private static async Task<ConnectedReadinessSession> CreateConnectedSessionAsync(int processId, string responseJson)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                while (server.IsConnected)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                    request.Should().NotBeNull();
                    if (request is null)
                    {
                        throw new InvalidOperationException("Expected a valid InspectorRequest payload.");
                    }

                    var response = new InspectorResponse
                    {
                        Id = request.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(responseJson),
                        Error = null
                    };

                    await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
                }
            }
            catch (EndOfStreamException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedReadinessSession(sessionManager, server, serverTask);
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        var field = typeof(SessionManager).GetField("_pipeClients", BindingFlags.Instance | BindingFlags.NonPublic);
        var pipeClients = field!.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>;
        if (pipeClients!.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }

    private sealed class ConnectedReadinessSession(SessionManager sessionManager, NamedPipeServerStream server, Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
                serverTask.GetAwaiter().GetResult();
            }
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }
    }
}
