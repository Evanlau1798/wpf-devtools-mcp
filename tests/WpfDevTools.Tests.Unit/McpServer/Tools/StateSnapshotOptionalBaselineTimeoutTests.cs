using System.IO.Pipes;
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
public sealed class StateSnapshotOptionalBaselineTimeoutTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task CaptureStateSnapshot_WhenBindingBaselineReturnsTimeoutPayload_ShouldLiftRecoveryMetadata()
    {
        const int processId = 51031;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    propertyName = "Width",
                    currentValue = "120",
                    hadLocalValue = true,
                    localValue = "120",
                    baseValueSource = "Local"
                }),
                CreateTimeoutPayloadJson(processId)
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            propertyNames = new[] { "Width" }
        }), CancellationToken.None));

        AssertTimeoutRecovery(result, "get_binding_errors", processId);
    }

    [Fact]
    public async Task CaptureStateSnapshot_WhenValidationBaselineReturnsTimeoutPayload_ShouldLiftRecoveryMetadata()
    {
        const int processId = 51032;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    propertyName = "Width",
                    currentValue = "120",
                    hadLocalValue = true,
                    localValue = "120",
                    baseValueSource = "Local"
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                }),
                CreateTimeoutPayloadJson(processId)
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            propertyNames = new[] { "Width" }
        }), CancellationToken.None));

        AssertTimeoutRecovery(result, "get_validation_errors", processId);
    }

    [Fact]
    public async Task CaptureStateSnapshot_WhenBindingBaselineReturnsRateLimitPayload_ShouldLiftBackoffRecovery()
    {
        const int processId = 51033;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    propertyName = "Width",
                    currentValue = "120",
                    hadLocalValue = true,
                    localValue = "120",
                    baseValueSource = "Local"
                }),
                CreateRateLimitPayloadJson()
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            propertyNames = new[] { "Width" }
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Contain("get_binding_errors");
        result.GetProperty("errorCode").GetString().Should().Be("RateLimitExceeded");
        result.GetProperty("availableTokens").GetInt32().Should().Be(0);
        result.GetProperty("retryAfterSeconds").GetInt32().Should().Be(11);
        result.GetProperty("recovery").GetProperty("retryAfterSeconds").GetInt32().Should().Be(11);
    }

    private static void AssertTimeoutRecovery(JsonElement result, string method, int processId)
    {
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Contain(method);
        result.GetProperty("errorCode").GetString().Should().Be("Timeout");
        result.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        result.GetProperty("processId").GetInt32().Should().Be(processId);
        result.GetProperty("timeoutSeconds").GetInt32().Should().Be(12);
    }

    private static string CreateTimeoutPayloadJson(int processId) =>
        JsonSerializer.Serialize(new
        {
            success = false,
            error = "Timeout connecting to Inspector Named Pipe",
            errorCode = "Timeout",
            recovery = new
            {
                suggestedAction = "Reconnect before retrying.",
                requiresReconnect = true,
                stateAfterTimeoutUnknown = true,
                processId,
                timeoutSeconds = 12
            }
        });

    private static string CreateRateLimitPayloadJson() =>
        JsonSerializer.Serialize(new
        {
            success = false,
            error = "Rate limit exceeded while reading baseline diagnostics.",
            errorCode = "RateLimitExceeded",
            availableTokens = 0,
            retryAfterSeconds = 11,
            retryAfter = "Wait 11 seconds before retrying capture_state_snapshot."
        });

    private static async Task<ConnectedStateSession> CreateConnectedSessionAsync(
        int processId,
        IReadOnlyList<string> resultJsonSequence)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                foreach (var resultJson in resultJsonSequence)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

                    var response = new InspectorResponse
                    {
                        Id = request!.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(resultJson),
                        Error = null
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
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplaceSessionManagerPipeClient(sessionManager, processId, client);

        return new ConnectedStateSession(sessionManager, server, serverTask);
    }

    private sealed class ConnectedStateSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask) : IDisposable
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
