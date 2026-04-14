using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public class InspectorHostObservabilityTests : IDisposable
{
    private readonly List<string> _logFilesToDelete = new();

    [Fact]
    public async Task InspectorHost_WhenHandlingRequest_ShouldReturnAndLogCorrelationId()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{pid}.log");
        _logFilesToDelete.Add(logPath);
        TryDelete(logPath);

        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClient(pid);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10), maxRetries: 5);
        connected.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.SendRequestAsync("ping", "obs-ping", new { }, cts.Token);

        response.Error.Should().BeNull();
        response.CorrelationId.Should().NotBeNullOrEmpty();

        host.Dispose();
        var content = await WaitForLogContentAsync(
            logPath,
            value => value.Contains(response.CorrelationId!, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        content.Should().Contain("REQUEST");
        content.Should().Contain("\"method\":\"ping\"");
        content.Should().Contain(response.CorrelationId!);
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
}
