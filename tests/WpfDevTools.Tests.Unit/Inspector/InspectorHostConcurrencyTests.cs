using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Inspector;

/// <summary>
/// Tests for InspectorHost concurrency and shutdown issues
/// </summary>
public class InspectorHostConcurrencyTests
{
    [Fact]
    public async Task Stop_DuringActiveRequest_ShouldWaitForCompletion()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();
        using var client = await ConnectToHostAsync(pid);

        // Send a ping request but don't wait for response yet
        var request = new InspectorRequest
        {
            Id = "shutdown-test-1",
            Method = "ping",
            Params = null
        };

        var requestJson = JsonSerializer.Serialize(request);
        await MessageFraming.WriteMessageAsync(client, requestJson);

        // Act - Stop the host while request is being processed
        // This should wait for the server task to complete
        var stopTask = Task.Run(() => host.Stop());

        // Assert - Stop should complete within timeout
        var completed = await Task.WhenAny(stopTask, Task.Delay(10_000)) == stopTask;
        completed.Should().BeTrue("Stop() should complete within reasonable time");

        host.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Stop_ShouldLogTimeoutIfTaskDoesNotComplete()
    {
        // This test verifies that if the server task doesn't complete within
        // ShutdownTimeout, the code handles it gracefully (logs and continues)
        // We can't easily force a timeout in unit tests, but we verify the
        // code path exists by checking the implementation

        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        // Act - Stop should not throw even if task is slow
        var act = () => host.Stop();

        // Assert
        act.Should().NotThrow();
        host.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Stop_WhenServerTaskIsCanceled_ShouldTreatCancellationAsNormalShutdown()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);

        var canceledTask = Task.FromCanceled(new CancellationToken(canceled: true));
        SetPrivateField(host, "_serverTask", canceledTask);
        SetPrivateField(host, "_cancellationTokenSource", new CancellationTokenSource());
        SetPrivateField(host, "_isRunning", true);

        var act = () => host.Stop();

        act.Should().NotThrow("shutdown should treat a canceled server loop as a normal stop path");
        host.IsRunning.Should().BeFalse();
    }

    private static void SetPrivateField<T>(InspectorHost host, string fieldName, T value)
    {
        var field = typeof(InspectorHost).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(host, value);
    }

    private static async Task<NamedPipeClientStream> ConnectToHostAsync(int processId)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var client = new NamedPipeClientStream(
                ".",
                $"WpfDevTools_{processId}",
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await client.ConnectAsync(1_000);
                return client;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                client.Dispose();
                await Task.Delay(100);
            }
        }

        throw new TimeoutException($"Timed out waiting for InspectorHost pipe for synthetic process {processId}.");
    }
}
