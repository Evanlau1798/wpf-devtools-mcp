using System.Text.Json;
using WpfDevTools.Shared.Messages;

namespace WpfDevTools.Inspector.Host;

public sealed partial class InspectorHost
{
    private InspectorRequest? DeserializeRequest(string requestJson)
    {
        try
        {
            return JsonSerializer.Deserialize<InspectorRequest>(requestJson, IpcSerializerOptions);
        }
        catch (JsonException ex)
        {
            LogError($"Invalid IPC request JSON: {ex.Message}");
            return null;
        }
    }
}
