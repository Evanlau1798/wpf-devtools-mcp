using System.IO.Pipes;
using System.Reflection;
using FluentAssertions;
using WpfDevTools.Inspector.Host;

namespace WpfDevTools.Tests.Unit.Inspector;

public partial class InspectorHostConcurrencyTests
{
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
