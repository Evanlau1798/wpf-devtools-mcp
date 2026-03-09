using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// MCP STDIO protocol client for E2E testing.
/// Communicates with the MCP Server via stdin/stdout using newline-delimited JSON (NDJSON).
/// The C# ModelContextProtocol SDK v1.0.0 StdioServerTransport reads JSON messages line-by-line.
/// </summary>
public sealed class McpStdioClient : IDisposable
{
    private Process? _serverProcess;
    private int _nextId;
    private readonly ConcurrentQueue<string> _stderrLines = new();

    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

    /// <summary>
    /// Captured stderr output from the MCP server (for diagnostics).
    /// </summary>
    public string ServerStderr => string.Join(Environment.NewLine, _stderrLines.ToArray());

    /// <summary>
    /// Start the MCP server process and perform protocol handshake.
    /// </summary>
    public async Task<JsonElement> StartAsync(string serverExePath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = serverExePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        _serverProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start MCP server process");

        _serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _stderrLines.Enqueue(e.Data);
        };
        _serverProcess.BeginErrorReadLine();

        // Brief wait for server process to spawn and initialize STDIO transport
        await Task.Delay(200, ct);

        if (_serverProcess.HasExited)
        {
            throw new InvalidOperationException(
                $"MCP server exited immediately with code {_serverProcess.ExitCode}. " +
                $"Stderr: {ServerStderr}");
        }

        var initResult = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "e2e-integration-test", version = "1.0.0" }
        }, timeoutMs: 30000, ct);

        await SendNotificationAsync("notifications/initialized");

        return initResult;
    }

    /// <summary>
    /// Call an MCP tool and return the parsed tool result JSON.
    /// </summary>
    public async Task<JsonElement> CallToolAsync(
        string toolName,
        object? arguments = null,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        var response = await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments = arguments ?? new { }
        }, timeoutMs, ct);

        return ExtractToolResult(response);
    }

    /// <summary>
    /// List all available MCP tools.
    /// </summary>
    public async Task<JsonElement> ListToolsAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync("tools/list", new { }, 30000, ct);
    }

    private async Task<JsonElement> SendRequestAsync(
        string method, object? parameters, int timeoutMs, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);

        var payload = parameters != null
            ? (object)new { jsonrpc = "2.0", id, method, @params = parameters }
            : new { jsonrpc = "2.0", id, method };

        await SendJsonLineAsync(payload);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var message = await ReadJsonLineAsync(cts.Token);

                if (message.TryGetProperty("id", out var responseId) &&
                    responseId.ValueKind == JsonValueKind.Number &&
                    responseId.GetInt32() == id)
                {
                    return message;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            var serverState = _serverProcess?.HasExited == true
                ? $"exited with code {_serverProcess.ExitCode}"
                : "running";

            throw new TimeoutException(
                $"Timed out ({timeoutMs}ms) waiting for response to '{method}' (id={id}). " +
                $"Server state: {serverState}. Stderr tail: {TruncateStderr(500)}");
        }

        throw new TimeoutException(
            $"Timed out waiting for response to '{method}' (id={id})");
    }

    private async Task SendNotificationAsync(string method)
    {
        await SendJsonLineAsync(new { jsonrpc = "2.0", method, @params = new { } });
    }

    /// <summary>
    /// Send a JSON message as a single line followed by newline (NDJSON format).
    /// </summary>
    private async Task SendJsonLineAsync(object payload)
    {
        EnsureRunning();

        var json = JsonSerializer.Serialize(payload);
        await _serverProcess!.StandardInput.WriteLineAsync(json);
        await _serverProcess.StandardInput.FlushAsync();
    }

    /// <summary>
    /// Read one JSON message line from stdout (NDJSON format).
    /// Uses Task.WhenAny to make the non-cancellable ReadLineAsync respect cancellation.
    /// </summary>
    private async Task<JsonElement> ReadJsonLineAsync(CancellationToken ct)
    {
        EnsureRunning();

        var readTask = _serverProcess!.StandardOutput.ReadLineAsync();
        var delayTask = Task.Delay(Timeout.Infinite, ct);

        var completedTask = await Task.WhenAny(readTask, delayTask);

        // If readTask completed (even if ct fired simultaneously), use its result
        // to avoid losing a message from the stream.
        if (completedTask == readTask || readTask.IsCompleted)
        {
            var line = await readTask;
            if (line == null)
            {
                throw new EndOfStreamException(
                    $"MCP server closed stdout. Server stderr: {TruncateStderr(300)}");
            }

            return JsonSerializer.Deserialize<JsonElement>(line);
        }

        // Cancellation won the race - throw to caller
        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException(ct);
    }

    private static JsonElement ExtractToolResult(JsonElement response)
    {
        if (response.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(
                $"MCP error: {error.GetRawText()}");
        }

        if (response.TryGetProperty("result", out var result) &&
            result.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array &&
            content.GetArrayLength() > 0)
        {
            var firstContent = content[0];
            if (firstContent.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return JsonSerializer.Deserialize<JsonElement>(text.GetString()!);
            }
        }

        return response;
    }

    private void EnsureRunning()
    {
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            var exitInfo = _serverProcess?.HasExited == true
                ? $"Exit code: {_serverProcess.ExitCode}. Stderr: {TruncateStderr(300)}"
                : "Process not started";

            throw new InvalidOperationException(
                $"MCP server process is not running. {exitInfo}");
        }
    }

    private string TruncateStderr(int maxLength)
    {
        var stderr = ServerStderr;
        return stderr.Length <= maxLength ? stderr : stderr.Substring(stderr.Length - maxLength);
    }

    public void Dispose()
    {
        if (_serverProcess != null)
        {
            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.StandardInput.Close();
                    if (!_serverProcess.WaitForExit(2000))
                    {
                        _serverProcess.Kill();
                        _serverProcess.WaitForExit(3000);
                    }
                }
            }
            catch
            {
                try { _serverProcess.Kill(); } catch { /* best effort */ }
            }
            finally
            {
                _serverProcess.Dispose();
            }
        }
    }
}
