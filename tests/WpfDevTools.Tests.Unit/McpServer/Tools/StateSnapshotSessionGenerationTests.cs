using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class StateSnapshotSessionGenerationTests
{
    [Fact]
    public void SaveStateSnapshot_ShouldPersistCurrentSessionGenerationOnStoredSnapshot()
    {
        const int processId = 51141;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);
        sessionManager.TryGetSessionGeneration(processId, out var sessionGeneration).Should().BeTrue();

        sessionManager.SaveStateSnapshot(
            processId,
            CreateStoredStateSnapshot("snapshot_generation", DateTimeOffset.UtcNow));

        sessionManager.TryGetStateSnapshot(processId, "snapshot_generation", out var snapshot).Should().BeTrue();
        var generationProperty = snapshot!.GetType().GetProperty("SessionGeneration");
        generationProperty.Should().NotBeNull("stored snapshots must be bound to the session generation that created them");
        generationProperty!.GetValue(snapshot).Should().Be(sessionGeneration);
    }

    [Fact]
    public async Task CaptureStateSnapshot_WhenSessionGenerationChangesBetweenSteps_ShouldFailWithoutSavingSnapshot()
    {
        const int processId = 51140;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);

        using var firstServer = new PipeResponseServer([
            JsonSerializer.Serialize(new
            {
                success = true,
                propertyName = "Width",
                currentValue = "120",
                hadLocalValue = true,
                localValue = "120",
                baseValueSource = "Local"
            })
        ]);
        await firstServer.AttachAsync(sessionManager, processId);

        var tool = new CaptureStateSnapshotTool(sessionManager);
        var captureTask = tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            propertyNames = new[] { "Width", "Height" }
        }), CancellationToken.None);

        await firstServer.WaitForResponsesWrittenAsync(1);

        using var secondServer = new PipeResponseServer([
            JsonSerializer.Serialize(new
            {
                success = true,
                propertyName = "Height",
                currentValue = "48",
                hadLocalValue = true,
                localValue = "48",
                baseValueSource = "Local"
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            })
        ]);

        sessionManager.RemoveSession(processId);
        await secondServer.AttachAsync(sessionManager, processId);

        var result = JsonSerializer.SerializeToElement(await captureTask);

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("NotConnected");
        sessionManager.TryGetActiveSnapshotId(processId, out _).Should().BeFalse();
    }

    private sealed class PipeResponseServer : IDisposable
    {
        private readonly IReadOnlyList<string> _responses;
        private readonly NamedPipeServerStream _server;
        private readonly TaskCompletionSource<int> _responsesWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serverTask;

        public PipeResponseServer(IReadOnlyList<string> responses)
        {
            _responses = responses;
            PipeName = CreateUniquePipeName();
            _server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            _serverTask = Task.Run(RunAsync);
        }

        public string PipeName { get; }

        public async Task AttachAsync(SessionManager sessionManager, int processId)
        {
            sessionManager.AddSession(processId);
            var client = new NamedPipeClient(processId, PipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplaceSessionManagerPipeClient(sessionManager, processId, client);
        }

        public async Task WaitForResponsesWrittenAsync(int responseCount)
        {
            while (await _responsesWritten.Task.WaitAsync(TimeSpan.FromSeconds(5)) < responseCount)
            {
            }
        }

        public void Dispose()
        {
            _server.Dispose();
            _serverTask.GetAwaiter().GetResult();
        }

        private async Task RunAsync()
        {
            try
            {
                await _server.WaitForConnectionAsync();
                for (var index = 0; index < _responses.Count; index++)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(_server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                    var response = new InspectorResponse
                    {
                        Id = request!.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(_responses[index])
                    };

                    await MessageFraming.WriteMessageAsync(
                        _server,
                        JsonSerializer.Serialize(response),
                        CancellationToken.None);
                    _responsesWritten.TrySetResult(index + 1);
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException)
            {
            }
        }
    }
}
