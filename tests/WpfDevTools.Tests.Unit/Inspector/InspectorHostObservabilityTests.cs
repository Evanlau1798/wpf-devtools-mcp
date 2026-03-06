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
        var pid = Random.Shared.Next(100_000, 999_999);
        var logPath = Path.Combine(Path.GetTempPath(), $"WpfDevTools_Inspector_{pid}.log");
        _logFilesToDelete.Add(logPath);
        TryDelete(logPath);

        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClient(pid);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));
        connected.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.SendRequestAsync("ping", "obs-ping", new { }, cts.Token);

        response.Error.Should().BeNull();
        response.CorrelationId.Should().NotBeNullOrEmpty();

        host.Dispose();
        await Task.Delay(200);

        File.Exists(logPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(logPath);
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
}
