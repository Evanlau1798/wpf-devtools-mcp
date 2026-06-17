using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

internal static class ConnectedWaitSessionBuilder
{
    internal static async Task<ConnectedWaitSession> CreateAsync<TState>(
        int processId,
        TState state,
        Func<InspectorRequest, TState, Task<object>> buildResultAsync,
        Action<InspectorRequest>? onRequest = null,
        List<(string method, bool settleBindings)>? requestPayloads = null,
        Action<InspectorRequest>? onResponse = null,
        int? maxRequestsPerMinute = null)
        where TState : class
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var requestMethods = new List<string>();
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
                    onRequest?.Invoke(request);

                    var result = await buildResultAsync(request, state);
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
                    onResponse?.Invoke(request);
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        SessionManager? sessionManager = null;
        try
        {
            sessionManager = maxRequestsPerMinute.HasValue
                ? new SessionManager(maxRequestsPerMinute.Value)
                : new SessionManager();
            sessionManager.AddSession(processId);

            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplacePipeClient(sessionManager, processId, client);

            return new ConnectedWaitSession(sessionManager, server, serverTask, requestMethods, requestPayloads);
        }
        catch
        {
            sessionManager?.Dispose();
            server.Dispose();
            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
    }
}

internal sealed class ConnectedWaitSession(
    SessionManager sessionManager,
    NamedPipeServerStream server,
    Task serverTask,
    List<string> requestMethods,
    List<(string method, bool settleBindings)>? requestPayloads = null) : IDisposable
{
    public SessionManager SessionManager { get; } = sessionManager;
    public IReadOnlyList<string> RequestMethods { get; } = requestMethods;
    public IReadOnlyList<(string method, bool settleBindings)> RequestPayloads { get; } =
        requestPayloads ?? [];

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
            catch (IOException)
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
