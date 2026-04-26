using System.Text.Json;
using System.IO;
using System.IO.Pipes;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class BatchQueryToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithSingleTarget_ShouldReturnSingleResponseShape()
    {
        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1" },
            new[] { "Width" },
            (elementId, propertyName, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName,
                currentValue = 120
            }),
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("currentValue").GetInt32().Should().Be(120);
        json.TryGetProperty("results", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithBatchTargets_ShouldReturnCorrelatedResults()
    {
        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1", "Button_2" },
            new[] { "Width", "Height" },
            (elementId, propertyName, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName,
                currentValue = $"{elementId}:{propertyName}"
            }),
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("resultCount").GetInt32().Should().Be(4);
        var results = json.GetProperty("results").EnumerateArray().ToArray();
        results.Should().HaveCount(4);
        results[0].GetProperty("elementId").GetString().Should().NotBeNullOrEmpty();
        results[0].GetProperty("propertyName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithEqualLengthElementAndPropertyBatches_ShouldReturnPairwiseResults()
    {
        var calls = new List<(string? elementId, string? propertyName)>();

        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1", "Button_2" },
            new[] { "Width", "Height" },
            (elementId, propertyName, _) =>
            {
                calls.Add((elementId, propertyName));
                return Task.FromResult<object>(new
                {
                    success = true,
                    propertyName,
                    currentValue = $"{elementId}:{propertyName}"
                });
            },
            CancellationToken.None,
            BatchQueryExecutor.CombinationMode.PairwiseOrBroadcast);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("resultCount").GetInt32().Should().Be(2);
        calls.Should().Equal(
            ("Button_1", "Width"),
            ("Button_2", "Height"));
    }

    [Fact]
    public async Task ExecuteAsync_WithMismatchedElementAndPropertyBatches_ShouldReturnStructuredError()
    {
        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1", "Button_2" },
            new[] { "Width", "Height", "Visibility" },
            (_, _, _) => Task.FromResult<object>(new { success = true }),
            CancellationToken.None,
            BatchQueryExecutor.CombinationMode.PairwiseOrBroadcast);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("same length");
    }

    [Fact]
    public async Task GetDpValueSourceTool_WithMixedSingleAndBatchInputs_ShouldReturnStructuredError()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(52001);
        var tool = new GetDpValueSourceTool(sessionManager);

        var result = await tool.ExecuteAsync(TestHelpers.ToJsonElement(new
        {
            processId = 52001,
            elementId = "Button_1",
            elementIds = new[] { "Button_2" },
            propertyName = "Width"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("elementId");
        json.GetProperty("error").GetString().Should().Contain("elementIds");
    }

    [Fact]
    public async Task GetDpValueSourceTool_WithEqualLengthElementAndPropertyBatches_ShouldIssuePairwiseRequests()
    {
        const int processId = 52002;
        using var session = await ConnectedBatchQuerySession.CreateAsync(processId);
        var tool = new GetDpValueSourceTool(session.SessionManager);

        var result = await tool.ExecuteAsync(TestHelpers.ToJsonElement(new
        {
            processId,
            elementIds = new[] { "SaveButton", "NameTextBox" },
            propertyNames = new[] { "IsEnabled", "Text" }
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue(json.GetRawText());
        json.GetProperty("resultCount").GetInt32().Should().Be(2);
        var queryRequests = session.Requests
            .Where(request => request.Item1 is not null || request.Item2 is not null)
            .ToArray();
        queryRequests.Should().Equal(
            ("SaveButton", "IsEnabled"),
            ("NameTextBox", "Text"));
    }

    private sealed class ConnectedBatchQuerySession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        List<(string? elementId, string? propertyName)> requests) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public IReadOnlyList<(string? elementId, string? propertyName)> Requests { get; } = requests;

        public static async Task<ConnectedBatchQuerySession> CreateAsync(int processId)
        {
            var pipeName = $"WpfDevTools_BatchQuery_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var requests = new List<(string? elementId, string? propertyName)>();

            var serverTask = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                try
                {
                    while (true)
                    {
                        var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                        var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                        var elementId = request.Params?.TryGetProperty("elementId", out var elementProperty) == true
                            ? elementProperty.GetString()
                            : null;
                        var propertyName = request.Params?.TryGetProperty("propertyName", out var propertyProperty) == true
                            ? propertyProperty.GetString()
                            : null;
                        requests.Add((elementId, propertyName));

                        var response = new InspectorResponse
                        {
                            Id = request.Id,
                            CorrelationId = request.CorrelationId,
                            Result = JsonSerializer.SerializeToElement(new
                            {
                                success = true,
                                propertyName,
                                currentValue = $"{elementId}:{propertyName}"
                            })
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
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            });

            var sessionManager = new SessionManager();
            DisableCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);

            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplacePipeClient(sessionManager, processId, client);

            return new ConnectedBatchQuerySession(sessionManager, server, serverTask, requests);
        }

        private static void DisableCleanupTimer(SessionManager sessionManager)
        {
            DisableSessionManagerCleanupTimer(sessionManager);
        }

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
                catch (IOException)
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

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
    }
}
