using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class WaitForDpChangeToolConcurrencyTests
{
    [Fact]
    public async Task Execute_ShouldPollViaShortRequestsSoConcurrentMutationCanComplete()
    {
        const int processId = 4242;
        using var connected = await CreateConnectedSessionAsync(processId);

        var waitTool = new WaitForDpChangeTool(connected.SessionManager);
        var mutateTool = new NoPiggybackModifyViewModelTool(connected.SessionManager);

        var waitTask = waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                expectedValue = JsonSerializer.SerializeToElement("after"),
                timeoutMs = 1000,
                pollIntervalMs = 50
            }),
            CancellationToken.None);

        await Task.Delay(120);

        using var mutateCts = new CancellationTokenSource(250);
        var mutateResult = await mutateTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Name",
                value = JsonSerializer.SerializeToElement("after")
            }),
            mutateCts.Token);

        var waitResult = await waitTask;

        var mutateJson = JsonSerializer.SerializeToElement(mutateResult);
        var waitJson = JsonSerializer.SerializeToElement(waitResult);

        mutateJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");

        connected.RequestMethods.Should().Contain("get_dp_value_source");
        connected.RequestMethods.Should().Contain("modify_viewmodel");
        connected.RequestMethods.Should().NotContain("wait_for_dp_change");
    }

    [Fact]
    public async Task Execute_WhenExpectedValueAppearsOnFinalRead_ShouldReturnReachedInsteadOfTimedOut()
    {
        const int processId = 4343;
        using var connected = await CreateBoundaryConnectedSessionAsync(processId);
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        var waitResult = await waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                expectedValue = JsonSerializer.SerializeToElement("after"),
                timeoutMs = 75,
                pollIntervalMs = 50
            }),
            CancellationToken.None);

        var waitJson = JsonSerializer.SerializeToElement(waitResult);

        waitJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        waitJson.GetProperty("currentValue").GetString().Should().Be("after");
    }

    private static async Task<ConnectedWaitSession> CreateConnectedSessionAsync(int processId)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var requestMethods = new List<string>();
        var state = new WaitServerState();
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                while (true)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                    requestMethods.Add(request.Method);

                    var result = await BuildResultAsync(request, state);
                    var response = new InspectorResponse
                    {
                        Id = request.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.SerializeToElement(result)
                    };

                    await MessageFraming.WriteMessageAsync(
                        server,
                        JsonSerializer.Serialize(response),
                        CancellationToken.None);
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedWaitSession(sessionManager, server, serverTask, requestMethods);
    }

    private static async Task<ConnectedWaitSession> CreateBoundaryConnectedSessionAsync(int processId)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var requestMethods = new List<string>();
        var state = new BoundaryWaitServerState();
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                while (true)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                    requestMethods.Add(request.Method);

                    var result = BuildBoundaryResult(request, state);
                    var response = new InspectorResponse
                    {
                        Id = request.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.SerializeToElement(result)
                    };

                    await MessageFraming.WriteMessageAsync(
                        server,
                        JsonSerializer.Serialize(response),
                        CancellationToken.None);
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedWaitSession(sessionManager, server, serverTask, requestMethods);
    }

    private static async Task<object> BuildResultAsync(InspectorRequest request, WaitServerState state)
    {
        switch (request.Method)
        {
            case "wait_for_dp_change":
                await Task.Delay(750);
                return new
                {
                    success = true,
                    changed = false,
                    timedOut = true,
                    observedChange = false,
                    matchedExpectedValueAtStart = false,
                    completionReason = "TimedOut",
                    propertyName = "Text",
                    initialValue = state.CurrentValue,
                    initialBaseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    baseValueSource = "Local",
                    elapsedMs = 750,
                    pollCount = 1
                };
            case "get_dp_value_source":
                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    effectiveValue = state.CurrentValue
                };
            case "modify_viewmodel":
                state.CurrentValue = ExtractRequestedValue(request.Params);
                return new
                {
                    success = true,
                    propertyName = "Name",
                    oldValue = "before",
                    newValue = state.CurrentValue
                };
            default:
                return new { success = true };
        }
    }

    private static object BuildBoundaryResult(InspectorRequest request, BoundaryWaitServerState state)
    {
        switch (request.Method)
        {
            case "get_dp_value_source":
                state.GetDpValueSourceCallCount++;
                state.CurrentValue = state.GetDpValueSourceCallCount >= 4 ? "after" : "before";
                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    effectiveValue = state.CurrentValue
                };
            default:
                return new { success = true };
        }
    }

    private static string ExtractRequestedValue(JsonElement? @params)
    {
        if (!@params.HasValue || !@params.Value.TryGetProperty("value", out var valueProperty))
        {
            return "after";
        }

        return valueProperty.ValueKind == JsonValueKind.String
            ? valueProperty.GetString() ?? "after"
            : valueProperty.GetRawText();
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

    private sealed class ConnectedWaitSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        List<string> requestMethods) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public IReadOnlyList<string> RequestMethods { get; } = requestMethods;

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
                try
                {
                    serverTask.GetAwaiter().GetResult();
                }
                catch (EndOfStreamException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }
    }

    private sealed class WaitServerState
    {
        public string CurrentValue { get; set; } = "before";
    }

    private sealed class BoundaryWaitServerState
    {
        public string CurrentValue { get; set; } = "before";
        public int GetDpValueSourceCallCount { get; set; }
    }

    private sealed class NoPiggybackModifyViewModelTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
    {
        public async Task<object> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            var (processId, parameters, error) = GenericPipeTool.ExtractElementPropertyAndValueParams(_sessionManager, arguments);
            if (error != null)
            {
                return error;
            }

            return await SendInspectorRequestWithoutPiggybackAsync(
                processId,
                "modify_viewmodel",
                parameters,
                cancellationToken);
        }
    }
}
