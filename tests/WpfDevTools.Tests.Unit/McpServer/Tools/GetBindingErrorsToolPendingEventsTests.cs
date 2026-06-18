using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class GetBindingErrorsToolPendingEventsTests
{
    [Fact]
    public async Task ExecuteAsync_WithPiggybackedBindingErrorEvents_ShouldTrimVerbosePendingEventFields()
    {
        const int processId = 51042;
        const string errorMessage = "BindingExpression path error: 'MissingName' property not found on object.";
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 1,
                errors = new[]
                {
                    new
                    {
                        eventType = "PathError",
                        elementId = "TextBox_1",
                        propertyName = "Text",
                        bindingPath = "MissingName",
                        message = errorMessage
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "BindingError",
                        timestampUtc = DateTimeOffset.UtcNow,
                        sourceKey = $"binding:TextBox_1:Text:{errorMessage}",
                        elementId = "TextBox_1",
                        propertyName = "Text",
                        newValue = errorMessage,
                        valueType = "System.String"
                    }
                }
            }));
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        await connected.ServerTask;

        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events");
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        result.GetProperty("pendingEventsOrigin").GetString().Should().Be("piggybackSharedBuffer");
        result.GetProperty("pendingEventsMayIncludePriorContext").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventsSuggestedAction").GetString().Should().Contain("drain_events");

        var pendingEvent = result.GetProperty("pendingEvents")[0];
        pendingEvent.GetProperty("eventType").GetString().Should().Be("BindingError");
        pendingEvent.GetProperty("elementId").GetString().Should().Be("TextBox_1");
        pendingEvent.TryGetProperty("sourceKey", out _).Should().BeFalse();
        pendingEvent.TryGetProperty("newValue", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithPiggybackedDpChangeEvents_ShouldPreserveUsefulFields()
    {
        const int processId = 51043;
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "DpChange",
                        timestampUtc = DateTimeOffset.UtcNow,
                        sourceKey = "dp:Button_1:Width:1",
                        elementId = "Button_1",
                        propertyName = "Width",
                        newValue = 222,
                        valueType = "System.Double"
                    }
                }
            }));
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        await connected.ServerTask;

        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events");
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        result.GetProperty("pendingEventsOrigin").GetString().Should().Be("piggybackSharedBuffer");
        result.GetProperty("pendingEventsMayIncludePriorContext").GetBoolean().Should().BeTrue();

        var pendingEvent = result.GetProperty("pendingEvents")[0];
        pendingEvent.GetProperty("eventType").GetString().Should().Be("DpChange");
        pendingEvent.GetProperty("elementId").GetString().Should().Be("Button_1");
        pendingEvent.GetProperty("propertyName").GetString().Should().Be("Width");
        pendingEvent.GetProperty("newValue").GetInt32().Should().Be(222);
        pendingEvent.TryGetProperty("sourceKey", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithPiggybackedPendingEvents_ShouldNotConsumeRateLimitForInternalDrain()
    {
        var processId = NextSyntheticProcessId();
        var rateLimiter = new CountingRateLimiterManager();
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            rateLimiter,
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[] { new { eventType = "DpChange", elementId = "Button_1" } }
            }));
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        await connected.ServerTask;

        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events");
        rateLimiter.AcquireCount.Should().Be(1);
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithPiggybackCleanupFailureAndNoPendingEvents_ShouldPreserveCleanupDiagnostics()
    {
        const int processId = 51044;
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0,
                cleanupIncomplete = true,
                cleanupFailureMessage = "cleanup failed",
                cleanupFailureType = "InvalidOperationException"
            }));
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        await connected.ServerTask;

        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events");
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("cleanupIncomplete").GetBoolean().Should().BeTrue();
        result.GetProperty("cleanupFailureMessage").GetString().Should().Be("cleanup failed");
        result.GetProperty("cleanupFailureType").GetString().Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task ExecuteAsync_WhenReplayLockIsBusyAndCallerTokenCancelsDuringBestEffortPiggyback_ShouldStillReturnPrimarySuccess()
    {
        const int processId = 51045;
        using var cancellation = new CancellationTokenSource();
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            method =>
            {
                if (method == "get_binding_errors")
                {
                    cancellation.Cancel();
                }
            },
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }));
        using var replayLock = await connected.SessionManager.AcquirePendingEventReplayLockAsync(processId, CancellationToken.None);
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            cancellation.Token));

        await connected.ServerTask;

        connected.RequestMethods.Should().Equal("get_binding_errors");
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPiggybackDrainReturnsNonSuccessPayload_ShouldKeepPrimarySuccessWithDiagnostics()
    {
        const int processId = 51046;
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }),
            JsonSerializer.Serialize(new
            {
                success = false,
                error = "drain failed",
                errorCode = "Timeout"
            }));
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        await connected.ServerTask;

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
        result.GetProperty("pendingEventsPiggybackFailed").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventsPiggybackFailureType").GetString().Should().Be("NonSuccessResponse");
        result.GetProperty("pendingEventsMayRemainBuffered").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventsPiggybackSuggestedAction").GetString().Should().Contain("drain_events");
    }

    [Fact]
    public async Task ExecuteAsync_WhenPiggybackDrainTimesOut_ShouldKeepPrimarySuccessWithDiagnostics()
    {
        const int processId = 51047;
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }));
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
        result.GetProperty("pendingEventsPiggybackFailed").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventsPiggybackFailureType").GetString().Should().Be("Timeout");
        result.GetProperty("pendingEventsMayRemainBuffered").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPiggybackDrainTransportResets_ShouldKeepPrimarySuccessWithDiagnostics()
    {
        const int processId = 51048;
        using var connected = await ConnectedBindingErrorsSession.CreateAsync(
            processId,
            onRequestReceived: async (method, server) =>
            {
                if (string.Equals(method, "drain_events", StringComparison.Ordinal))
                {
                    await server.DisposeAsync();
                }
            },
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0
            }));
        var tool = new GetBindingErrorsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        await connected.ServerTask;

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
        result.GetProperty("pendingEventsPiggybackFailed").GetBoolean().Should().BeTrue();
        result.GetProperty("pendingEventsPiggybackFailureType").GetString().Should().Be("TransportReset");
        result.GetProperty("pendingEventsMayRemainBuffered").GetBoolean().Should().BeTrue();
    }

    private sealed class ConnectedBindingErrorsSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        List<string> requestMethods) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public Task ServerTask { get; } = serverTask;
        public IReadOnlyList<string> RequestMethods { get; } = requestMethods;

        public static Task<ConnectedBindingErrorsSession> CreateAsync(int processId, params string[] responses) =>
            CreateAsync(processId, rateLimiter: null, onResponseWritten: null, responses);

        public static Task<ConnectedBindingErrorsSession> CreateAsync(
            int processId,
            IRateLimiterManager rateLimiter,
            params string[] responses) =>
            CreateAsync(processId, rateLimiter, onResponseWritten: null, responses);

        public static Task<ConnectedBindingErrorsSession> CreateAsync(
            int processId,
            Func<string, NamedPipeServerStream, Task>? onRequestReceived,
            params string[] responses) =>
            CreateAsync(processId, rateLimiter: null, onResponseWritten: null, onRequestReceived, responses);

        public static async Task<ConnectedBindingErrorsSession> CreateAsync(
            int processId,
            Action<string>? onResponseWritten,
            params string[] responses) =>
            await CreateAsync(processId, rateLimiter: null, onResponseWritten, onRequestReceived: null, responses);

        private static Task<ConnectedBindingErrorsSession> CreateAsync(
            int processId,
            IRateLimiterManager? rateLimiter,
            Action<string>? onResponseWritten,
            params string[] responses) =>
            CreateAsync(processId, rateLimiter, onResponseWritten, onRequestReceived: null, responses);

        private static async Task<ConnectedBindingErrorsSession> CreateAsync(
            int processId,
            IRateLimiterManager? rateLimiter,
            Action<string>? onResponseWritten,
            Func<string, NamedPipeServerStream, Task>? onRequestReceived,
            params string[] responses)
        {
            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var requestMethods = new List<string>();
            var responseQueue = new Queue<string>(responses);

            var serverTask = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                try
                {
                    while (server.IsConnected && responseQueue.Count > 0)
                    {
                        var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                        var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                        request.Should().NotBeNull();
                        requestMethods.Add(request!.Method);
                        if (onRequestReceived is not null)
                        {
                            await onRequestReceived(request.Method, server);
                        }

                        var response = new InspectorResponse
                        {
                            Id = request.Id,
                            CorrelationId = request.CorrelationId,
                            Result = JsonSerializer.Deserialize<JsonElement>(responseQueue.Dequeue())
                        };

                        await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
                        onResponseWritten?.Invoke(request.Method);
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

            var sessionManager = rateLimiter is null ? new SessionManager() : new SessionManager(rateLimiter);
            DisableSessionManagerCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);
            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplaceSessionManagerPipeClient(sessionManager, processId, client);

            return new ConnectedBindingErrorsSession(sessionManager, server, serverTask, requestMethods);
        }

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
                try
                {
                    ServerTask.GetAwaiter().GetResult();
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

    private sealed class CountingRateLimiterManager : IRateLimiterManager, IRateLimiterStatusProvider
    {
        private int _acquireCount;

        public int AcquireCount => Volatile.Read(ref _acquireCount);

        public bool TryAcquire(int processId)
        {
            Interlocked.Increment(ref _acquireCount);
            return true;
        }

        public RateLimitStatus TryAcquireWithStatus(int processId)
        {
            Interlocked.Increment(ref _acquireCount);
            return new RateLimitStatus(true, int.MaxValue, TimeSpan.Zero);
        }

        public void RemoveSession(int processId)
        {
        }

        public int GetAvailableTokens(int processId) => int.MaxValue;

        public TimeSpan GetRetryAfter(int processId) => TimeSpan.Zero;
    }
}
