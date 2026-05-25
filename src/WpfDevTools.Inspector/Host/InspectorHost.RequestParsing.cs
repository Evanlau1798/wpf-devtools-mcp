using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Validation;

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

        if (!TryReadRequiredString(
            root,
            "id",
            BoundaryStringLimits.MaxJsonRpcIdLength,
            out var id,
            out var idError))
        {
            errorMessage = $"Invalid request: id {idError}.";
            return false;
        }

        requestId = id;

        if (!TryReadRequiredString(
            root,
            "method",
            BoundaryStringLimits.MaxInspectorMethodLength,
            out _,
            out var methodError))
        {
            errorMessage = $"Invalid request: method {methodError}.";
            return false;
        }

        if (!TryReadOptionalString(
            root,
            "correlationId",
            BoundaryStringLimits.MaxCorrelationIdLength,
            out var correlationIdError))
        {
            errorMessage = $"Invalid request: correlationId {correlationIdError}.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryReadRequiredString(
        JsonElement root,
        string propertyName,
        int maxLength,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = "must be a non-empty string";
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

        if (stringValue!.Length > maxLength)
        {
            error = $"must be at most {maxLength} characters";
            return false;
        }

        value = stringValue!;
        return true;
    }

    private static bool TryReadOptionalString(
        JsonElement root,
        string propertyName,
        int maxLength,
        out string error)
    {
        error = "must be a string when provided";
        if (!TryGetRequestProperty(root, propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var stringValue = property.GetString() ?? string.Empty;
        if (stringValue.Length > maxLength)
        {
            error = $"must be at most {maxLength} characters";
            return false;
        }

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
