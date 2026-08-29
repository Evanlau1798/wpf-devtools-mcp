using System.Text.Json;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed partial class McpStdioClient
{
    private Func<JsonElement, Task<object>>? _serverRequestHandler;

    public async Task<JsonElement> StartWithElicitationAsync(
        string serverExePath,
        IReadOnlyDictionary<string, string>? environmentVariables,
        Func<JsonElement, Task<object>> serverRequestHandler,
        CancellationToken ct = default)
    {
        _serverRequestHandler = serverRequestHandler
            ?? throw new ArgumentNullException(nameof(serverRequestHandler));
        return await StartAsync(serverExePath, environmentVariables, supportsElicitation: true, ct);
    }

    private async Task HandleServerRequestAsync(JsonElement request, CancellationToken ct)
    {
        var id = request.GetProperty("id").Clone();
        if (_serverRequestHandler is null)
        {
            await SendJsonLineAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new { code = -32601, message = "Client request handler is unavailable." }
            }, ct);
            return;
        }

        var result = await _serverRequestHandler(request);
        await SendJsonLineAsync(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }, ct);
    }
}
