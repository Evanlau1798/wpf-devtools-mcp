using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Host.Handlers;

/// <summary>
/// Contract regression tests ensuring response shapes for event-related
/// operations remain stable across refactors.
/// </summary>
[Collection("ToolCallHelperState")]
public sealed class EventHandlersContractTests : IDisposable
{
    private static readonly TimeSpan PipeBackedServerTimeout = TimeSpan.FromSeconds(10);

    public void Dispose()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [StaFact]
    public void FireRoutedEvent_ClickOnButtonWithCommand_ShouldReturnUsedOnClickFlag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var commandExecuted = false;
        var button = new System.Windows.Controls.Button
        {
            Command = new TestRelayCommand(() => commandExecuted = true)
        };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "Click", null);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("usedOnClick").GetBoolean().Should().BeTrue(
            "fire_routed_event('Click') on ButtonBase must include usedOnClick: true");
        commandExecuted.Should().BeTrue(
            "OnClick() path should execute the attached ICommand");
    }

    [StaFact]
    public void FireRoutedEvent_NonClickEvent_ShouldNotReturnUsedOnClickFlag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "LostFocus", null);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("usedOnClick", out _).Should().BeFalse(
            "Non-Click events must not include usedOnClick in the response");
    }

    [Fact]
    public async Task FireRoutedEvent_Navigation_ShouldRecommendSafeVerificationPath()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                eventName = "Click",
                message = "Invoked OnClick path",
                usedOnClick = true
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton"), ("eventName", "Click")),
            CancellationToken.None,
            toolName: "fire_routed_event");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_ui_summary");
        nextSteps.EnumerateArray().Select(item => item.GetProperty("tool").GetString()).Should().NotContain("trace_routed_events");
    }

    [Fact]
    public async Task GetEventHandlers_WithIncompleteClickInspection_ShouldRecommendCommandFallback()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "SaveButton",
                eventName = "Click",
                handlerCount = 0,
                mayBeIncomplete = true,
                handlers = Array.Empty<object>()
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton"), ("eventName", "Click")),
            CancellationToken.None,
            toolName: "get_event_handlers");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_commands");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("SaveButton");
    }

    [Fact]
    public async Task GetEventHandlersWrapper_WithIncompleteClickInspection_ShouldRecommendCommandFallback()
    {
        const int processId = 62041;
        var pipeName = CreateUniquePipeName();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        using var serverTimeout = new CancellationTokenSource(PipeBackedServerTimeout);
        var serverTask = RunIncompleteClickInspectionServerAsync(server, serverTimeout.Token);

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplaceSessionManagerPipeClient(sessionManager, processId, client);

        var result = await EventMcpTools.GetEventHandlers(
            sessionManager,
            eventName: "Click",
            processId: processId,
            elementId: "SaveButton");

        await AwaitPipeBackedServerTaskAsync(serverTask, serverTimeout.Token);

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        navigation.GetProperty("recommended")[0].GetProperty("tool").GetString().Should().Be("get_commands");
        navigation.GetProperty("recommended")[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("SaveButton");
    }

    [Fact]
    public void PipeBackedContractServerTask_ShouldUseBoundedCancellation()
    {
        var source = File.ReadAllText(GetCurrentSourcePath()).Replace("\r\n", "\n");
        var unboundedWaitForConnection = "await server." + "WaitForConnectionAsync();";
        var unboundedRead = "await MessageFraming." +
            "ReadMessageAsync(server, CancellationToken.None)";
        var unboundedWrite = "await MessageFraming." +
            "WriteMessageAsync(\n" +
            "                server,\n" +
            "                JsonSerializer.Serialize(response),\n" +
            "                CancellationToken.None)";
        var unboundedTaskAwait = "await " + "serverTask;";

        source.Should().NotContain(unboundedWaitForConnection);
        source.Should().NotContain(unboundedRead);
        source.Should().NotContain(unboundedWrite);
        source.Should().NotContain(unboundedTaskAwait);
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_ZeroEventPayload_ShouldIncludeStableDiagnosticsReasonCode()
    {
        var finder = new ElementFinder();
        var handler = new EventHandlers(new EventAnalyzer(finder));
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var startResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 100,
                allowShortStartDuration = true
            }),
            CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        await Task.Delay(180);

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);
        var payload = JsonSerializer.SerializeToElement(getResult);

        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().BeOneOf(
            "captureWindowTooShort",
            "eventNotRaised",
            "filterMismatch",
            "cleanupFailed");
    }

    [Fact]
    public async Task DrainEvents_WhenPostDrainCleanupReportsFailure_ShouldPreservePayloadAndSurfaceCleanupIncomplete()
    {
        var handler = new EventHandlers(
            new EventAnalyzer(new ElementFinder()),
            () => new InvalidOperationException("cleanup failed"));

        var result = await handler.HandleAsync(
            "drain_events",
            JsonSerializer.SerializeToElement(new { maxEvents = 5 }),
            CancellationToken.None);
        var payload = JsonSerializer.SerializeToElement(result);

        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("pendingEventCount").GetInt32().Should().Be(0);
        payload.GetProperty("droppedEventCount").GetInt32().Should().Be(0);
        payload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeTrue();
        payload.GetProperty("cleanupFailureType").GetString().Should().Be(nameof(InvalidOperationException));
        payload.GetProperty("cleanupFailureMessage").GetString().Should().Be("cleanup failed");
    }

    private sealed class TestRelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public TestRelayCommand(Action execute) => _execute = execute;
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }

    private static string GetCurrentSourcePath([CallerFilePath] string path = "") => path;

    private static Task RunIncompleteClickInspectionServerAsync(
        NamedPipeServerStream server,
        CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cancellationToken);
            var requestJson = await MessageFraming.ReadMessageAsync(server, cancellationToken);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
            request.Should().NotBeNull();

            var response = new InspectorResponse
            {
                Id = request!.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    elementId = "SaveButton",
                    eventName = "Click",
                    handlerCount = 0,
                    mayBeIncomplete = true,
                    handlers = Array.Empty<object>()
                })
            };

            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                cancellationToken);
        }, cancellationToken);

    private static async Task AwaitPipeBackedServerTaskAsync(Task serverTask, CancellationToken cancellationToken)
    {
        try
        {
            await serverTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Pipe-backed contract server task did not complete within {PipeBackedServerTimeout.TotalSeconds:0} seconds.",
                ex);
        }
    }
}
