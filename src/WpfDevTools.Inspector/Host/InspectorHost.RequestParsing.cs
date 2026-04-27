using System.Text.Json;
using WpfDevTools.Shared.Messages;

namespace WpfDevTools.Inspector.Host;

public sealed partial class InspectorHost
{
    private const string UnknownRequestId = "unknown";

    private RequestParseResult DeserializeRequest(string requestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(
                requestJson,
                new JsonDocumentOptions { MaxDepth = IpcSerializerOptions.MaxDepth });

            if (!TryValidateRequestShape(
                document.RootElement,
                out var requestId,
                out var errorMessage))
            {
                return RequestParseResult.Invalid(requestId, errorMessage);
            }

            var request = document.RootElement.Deserialize<InspectorRequest>(IpcSerializerOptions);
            return request == null
                ? RequestParseResult.Invalid(UnknownRequestId, "Invalid request: JSON value must be an object.")
                : RequestParseResult.Valid(request);
        }
        catch (JsonException ex)
        {
            LogError($"Invalid IPC request JSON: {ex.Message}");
            return RequestParseResult.Invalid(UnknownRequestId, "Invalid request format");
        }
    }

    private static bool TryValidateRequestShape(
        JsonElement root,
        out string requestId,
        out string errorMessage)
    {
        requestId = UnknownRequestId;

        if (root.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "Invalid request: JSON value must be an object.";
            return false;
        }

        if (!TryReadRequiredString(root, "id", out var id))
        {
            errorMessage = "Invalid request: id must be a non-empty string.";
            return false;
        }

        requestId = id;

        if (!TryReadRequiredString(root, "method", out _))
        {
            errorMessage = "Invalid request: method must be a non-empty string.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryReadRequiredString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!TryGetRequestProperty(root, propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var stringValue = property.GetString();
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        value = stringValue!;
        return true;
    }

    private static bool TryGetRequestProperty(
        JsonElement root,
        string propertyName,
        out JsonElement property)
    {
        if (root.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        if (!IpcSerializerOptions.PropertyNameCaseInsensitive)
        {
            return false;
        }

        foreach (var jsonProperty in root.EnumerateObject())
        {
            if (string.Equals(
                jsonProperty.Name,
                propertyName,
                StringComparison.OrdinalIgnoreCase))
            {
                property = jsonProperty.Value;
                return true;
            }
        }

        return false;
    }

    private readonly record struct RequestParseResult(
        InspectorRequest? Request,
        string RequestId,
        string ErrorMessage)
    {
        public static RequestParseResult Valid(InspectorRequest request) =>
            new(request, request.Id, string.Empty);

        public static RequestParseResult Invalid(string requestId, string errorMessage) =>
            new(null, requestId, errorMessage);
    }
}
