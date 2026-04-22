using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// MCP STDIO protocol client for E2E testing.
/// Communicates with the MCP Server via stdin/stdout using newline-delimited JSON (NDJSON).
/// The C# ModelContextProtocol SDK v1.0.0 StdioServerTransport reads JSON messages line-by-line.
/// </summary>
public sealed class McpStdioClient : IDisposable
{
    private const string E2eRateLimitOverride = "2000";
    private Process? _serverProcess;
    private int _nextId;
    private readonly ConcurrentQueue<string> _stderrLines = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingResponses = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _readerCts = new();
    private Task? _readerTask;

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
        psi.Environment[McpServerConfiguration.RateLimitRequestsPerMinuteEnvVar] = E2eRateLimitOverride;

        _serverProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start MCP server process");

        _serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _stderrLines.Enqueue(e.Data);
        };
        _serverProcess.BeginErrorReadLine();
        _readerTask = Task.Run(() => RunReadLoopAsync(_readerCts.Token), _readerCts.Token);

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
    /// Call an MCP tool and return the raw JSON-RPC response envelope.
    /// </summary>
    public async Task<JsonElement> CallToolEnvelopeAsync(
        string toolName,
        object? arguments = null,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        return await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments = arguments ?? new { }
        }, timeoutMs, ct);
    }

    /// <summary>
    /// List all available MCP tools.
    /// </summary>
    public async Task<JsonElement> ListToolsAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync("tools/list", new { }, 30000, ct);
    }

    /// <summary>
    /// List all available MCP resources.
    /// </summary>
    public async Task<JsonElement> ListResourcesAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync("resources/list", new { }, 30000, ct);
    }

    /// <summary>
    /// Read an MCP resource by URI.
    /// </summary>
    public async Task<JsonElement> ReadResourceAsync(string uri, CancellationToken ct = default)
    {
        return await SendRequestAsync("resources/read", new { uri }, 30000, ct);
    }

    private async Task<JsonElement> SendRequestAsync(
        string method, object? parameters, int timeoutMs, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var responseTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingResponses.TryAdd(id, responseTcs))
        {
            throw new InvalidOperationException($"Duplicate MCP request id allocation: {id}");
        }

        var payload = parameters != null
            ? (object)new { jsonrpc = "2.0", id, method, @params = parameters }
            : new { jsonrpc = "2.0", id, method };

        try
        {
            await SendJsonLineAsync(payload);
        }
        catch
        {
            _pendingResponses.TryRemove(id, out _);
            throw;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            var completed = await Task.WhenAny(responseTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            if (completed == responseTcs.Task)
            {
                return await responseTcs.Task;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _pendingResponses.TryRemove(id, out _);
            var serverState = _serverProcess?.HasExited == true
                ? $"exited with code {_serverProcess.ExitCode}"
                : "running";

            throw new TimeoutException(
                $"Timed out ({timeoutMs}ms) waiting for response to '{method}' (id={id}). " +
                $"Server state: {serverState}. Stderr tail: {TruncateStderr(500)}");
        }

        _pendingResponses.TryRemove(id, out _);
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
        await _writeLock.WaitAsync();
        try
        {
            await _serverProcess!.StandardInput.WriteLineAsync(json);
            await _serverProcess.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task RunReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                EnsureRunning();

                var line = await _serverProcess!.StandardOutput.ReadLineAsync(ct);
                if (line == null)
                {
                    throw new EndOfStreamException(
                        $"MCP server closed stdout. Server stderr: {TruncateStderr(300)}");
                }

                var message = JsonSerializer.Deserialize<JsonElement>(line);
                if (!message.TryGetProperty("id", out var responseId) ||
                    responseId.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (_pendingResponses.TryRemove(responseId.GetInt32(), out var pending))
                {
                    pending.TrySetResult(message);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            foreach (var pending in _pendingResponses.ToArray())
            {
                if (_pendingResponses.TryRemove(pending.Key, out var tcs))
                {
                    tcs.TrySetException(ex);
                }
            }
        }
    }

    private static JsonElement ExtractToolResult(JsonElement response)
    {
        if (response.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(
                $"MCP error: {error.GetRawText()}");
        }

        if (!response.TryGetProperty("result", out var result))
        {
            return response;
        }

        if (result.TryGetProperty("structuredContent", out var structuredContent) &&
            structuredContent.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            return structuredContent;
        }

        if (result.TryGetProperty("content", out var content) &&
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
        _readerCts.Cancel();

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

        try
        {
            _readerTask?.GetAwaiter().GetResult();
        }
        catch
        {
        }

        _readerCts.Dispose();
        _writeLock.Dispose();
    }
}
