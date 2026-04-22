using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public class InspectorHostObservabilityTests : IDisposable
{
    private readonly List<string> _logFilesToDelete = new();

    [Fact]
    public async Task InspectorHost_WhenHandlingRequest_WithRequestLoggingEnabled_ShouldReturnAndLogCorrelationId()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{pid}.log");
        _logFilesToDelete.Add(logPath);
        TryDelete(logPath);

        using var host = new InspectorHost(pid, FileLogLevel.Info);
        host.Start();

        var response = await SendPingAsync(pid, requestId: "obs-ping", correlationId: "obs-correlation");

        response.Error.Should().BeNull();
        response.CorrelationId.Should().Be("obs-correlation");

        host.Dispose();
        var content = await WaitForLogContentAsync(
            logPath,
            value => value.Contains("obs-correlation", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        content.Should().Contain("REQUEST");
        content.Should().Contain("\"method\":\"ping\"");
        content.Should().Contain("obs-correlation");
    }

    [Fact]
    public async Task InspectorHost_WhenHandlingRequest_WithDefaultLoggingLevel_ShouldNotWriteRequestEntry()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{pid}.log");
        _logFilesToDelete.Add(logPath);
        TryDelete(logPath);

        using var host = new InspectorHost(pid);
        host.Start();

        var response = await SendPingAsync(pid, requestId: "obs-ping-default", correlationId: "obs-correlation-default");

        response.Error.Should().BeNull();
        response.CorrelationId.Should().Be("obs-correlation-default");

        host.Dispose();

        if (!File.Exists(logPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(logPath);
        content.Should().NotContain("REQUEST");
        content.Should().NotContain("\"method\":\"ping\"");
        content.Should().NotContain("obs-correlation-default");
    }

    public void Dispose()
    {
        foreach (var logFile in _logFilesToDelete)
        {
            TryDelete(logFile);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static async Task<string> WaitForLogContentAsync(
        string path,
        Func<string, bool> condition,
        TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path);
                if (condition(content))
                {
                    return content;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for log content at '{path}'.");
    }

    private static async Task<InspectorResponse> SendPingAsync(int pid, string requestId, string correlationId)
    {
        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5_000);

        var request = new InspectorRequest
        {
            Id = requestId,
            Method = "ping",
            Params = null,
            CorrelationId = correlationId
        };

        var requestJson = JsonSerializer.Serialize(request);
        await MessageFraming.WriteMessageAsync(client, requestJson);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        return JsonSerializer.Deserialize<InspectorResponse>(responseJson)
            ?? throw new InvalidOperationException("Failed to deserialize ping response.");
    }
}
