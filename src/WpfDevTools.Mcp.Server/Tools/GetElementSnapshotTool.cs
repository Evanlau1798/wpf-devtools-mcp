using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class GetElementSnapshotTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    private static readonly string[] SnapshotPropertyNames = ["Text", "Content", "Visibility", "IsEnabled", "Opacity"];

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(elementId))
        {
            return CreateMissingParamError("elementId");
        }

        var includeProperties = ParseStringArrayParam(arguments, "includeProperties");
        var identity = await GetIdentityAsync(processId, elementId, cancellationToken).ConfigureAwait(false);
        if (identity.error != null)
        {
            return identity.error;
        }

        var dataContextType = await GetDataContextTypeAsync(processId, elementId, cancellationToken).ConfigureAwait(false);
        if (dataContextType.error != null)
        {
            return dataContextType.error;
        }

        var bindings = await GetInspectorPayloadAsync(processId, "get_bindings", new { elementId }, cancellationToken).ConfigureAwait(false);
        if (bindings.error != null)
        {
            return bindings.error;
        }

        var validation = await GetInspectorPayloadAsync(processId, "get_validation_errors", new { elementId }, cancellationToken).ConfigureAwait(false);
        if (validation.error != null)
        {
            return validation.error;
        }

        var styles = await GetInspectorPayloadAsync(processId, "get_applied_styles", new { elementId, compact = true }, cancellationToken).ConfigureAwait(false);
        if (styles.error != null)
        {
            return styles.error;
        }

        var layout = await GetInspectorPayloadAsync(processId, "get_layout_info", new { elementId }, cancellationToken).ConfigureAwait(false);
        if (layout.error != null)
        {
            return layout.error;
        }

        var properties = await GetPropertySnapshotAsync(processId, elementId, includeProperties, cancellationToken).ConfigureAwait(false);
        var bindingsPayload = GetRequiredPayload(bindings.payload);
        var validationPayload = GetRequiredPayload(validation.payload);
        var stylesPayload = GetRequiredPayload(styles.payload);
        var layoutPayload = GetRequiredPayload(layout.payload);

        return new
        {
            success = true,
            elementId,
            elementType = identity.elementType,
            elementName = identity.elementName,
            dataContextType = dataContextType.value,
            properties,
            bindings = bindingsPayload.TryGetProperty("bindings", out var bindingsValue) ? bindingsValue.Clone() : JsonSerializer.SerializeToElement(Array.Empty<object>()),
            validationErrors = validationPayload.TryGetProperty("errors", out var validationErrors) ? validationErrors.Clone() : JsonSerializer.SerializeToElement(Array.Empty<object>()),
            style = stylesPayload.Clone(),
            layout = layoutPayload.Clone()
        };
    }

    private async Task<(string? elementType, string? elementName, object? error)> GetIdentityAsync(
        int processId,
        string elementId,
        CancellationToken cancellationToken)
    {
        var result = await GetInspectorPayloadAsync(
            processId,
            "get_visual_tree",
            new { elementId, depth = 0, compact = true },
            cancellationToken).ConfigureAwait(false);

        if (result.error != null)
        {
            return (null, null, result.error);
        }

        var payload = GetRequiredPayload(result.payload);
        var tree = payload.GetProperty("tree");
        return (
            GetOptionalString(tree, "type"),
            GetOptionalString(tree, "name"),
            null);
    }

    private async Task<(string? value, object? error)> GetDataContextTypeAsync(
        int processId,
        string elementId,
        CancellationToken cancellationToken)
    {
        var result = await GetInspectorPayloadAsync(
            processId,
            "get_datacontext_chain",
            new { elementId },
            cancellationToken).ConfigureAwait(false);

        if (result.error != null)
        {
            return (null, result.error);
        }

        var payload = GetRequiredPayload(result.payload);
        if (!payload.TryGetProperty("chain", out var chain) || chain.ValueKind != JsonValueKind.Array || chain.GetArrayLength() == 0)
        {
            return (null, null);
        }

        var first = chain[0];
        return (GetOptionalString(first, "dataContextType"), null);
    }

    private async Task<Dictionary<string, object?>> GetPropertySnapshotAsync(
        int processId,
        string elementId,
        string[]? includeProperties,
        CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propertyName in BuildSnapshotPropertyNames(includeProperties))
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                "get_dp_value_source",
                new { elementId, propertyName },
                cancellationToken).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                if (string.Equals(GetOptionalString(response, "errorCode"), ToolErrorCode.PropertyNotFound.ToString(), StringComparison.Ordinal))
                {
                    continue;
                }

                continue;
            }

            properties[propertyName] = new
            {
                currentValue = GetOptionalString(response, "currentValue"),
                baseValueSource = GetOptionalString(response, "baseValueSource")
            };
        }

        return properties;
    }

    private static IReadOnlyList<string> BuildSnapshotPropertyNames(string[]? includeProperties)
    {
        var ordered = new List<string>(SnapshotPropertyNames.Length + (includeProperties?.Length ?? 0));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var propertyName in SnapshotPropertyNames)
        {
            if (seen.Add(propertyName))
            {
                ordered.Add(propertyName);
            }
        }

        if (includeProperties == null)
        {
            return ordered;
        }

        foreach (var propertyName in includeProperties)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                continue;
            }

            var normalized = propertyName.Trim();
            if (seen.Add(normalized))
            {
                ordered.Add(normalized);
            }
        }

        return ordered;
    }

    private async Task<(JsonElement? payload, object? error)> GetInspectorPayloadAsync(
        int processId,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            method,
            parameters,
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (null, CreateStepFailure(method, response));
        }

        return (response, null);
    }

    private static bool IsSuccess(JsonElement response) =>
        response.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static JsonElement GetRequiredPayload(JsonElement? payload) =>
        payload ?? throw new InvalidOperationException("Inspector payload was unexpectedly null after a successful response.");

    private static ToolErrorPayload CreateStepFailure(string method, JsonElement response)
    {
        return new ToolErrorPayload
        {
            Error = response.TryGetProperty("error", out var errorProperty)
                ? $"Failed during {method} while building element snapshot. {errorProperty.GetString()}".Trim()
                : $"Failed during {method} while building element snapshot.",
            ErrorCode = response.TryGetProperty("errorCode", out var errorCodeProperty)
                ? errorCodeProperty.GetString() ?? ToolErrorCode.OperationFailed.ToString()
                : ToolErrorCode.OperationFailed.ToString(),
            Hint = response.TryGetProperty("hint", out var hintProperty)
                ? hintProperty.GetString()
                : $"Inspect the failing {method} step and refresh the target element before retrying get_element_snapshot.",
            ErrorData = response.Clone()
        };
    }
}
