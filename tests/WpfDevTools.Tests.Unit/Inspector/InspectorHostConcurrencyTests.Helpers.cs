using System.IO.Pipes;
using System.Reflection;
using FluentAssertions;
using WpfDevTools.Inspector.Host;

namespace WpfDevTools.Tests.Unit.Inspector;

public partial class InspectorHostConcurrencyTests
{
    private static Task RunSignaled(Action action, ManualResetEventSlim entered)
        => Task.Run(() =>
        {
            entered.Set();
            action();
        });

    private static void SetPrivateField<T>(InspectorHost host, string fieldName, T value)
    {
        var field = typeof(InspectorHost).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(host, value);
    }

    private static T GetPrivateField<T>(InspectorHost host, string fieldName)
    {
        var field = typeof(InspectorHost).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T)field!.GetValue(host)!;
    }

    private static async Task<NamedPipeClientStream> ConnectToHostAsync(int processId)
    {
        var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{processId}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(5_000);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
