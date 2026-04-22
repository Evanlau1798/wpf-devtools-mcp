using System.Text.Json;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles aggregated element snapshot requests by composing existing in-process handlers.
/// </summary>
public sealed class ElementSnapshotHandlers : IRequestHandler
{
    private static readonly string[] DefaultPropertyNames = ["Text", "Content", "Visibility", "IsEnabled", "Opacity"];

    private readonly IRequestHandler _treeHandlers;
    private readonly IRequestHandler _bindingHandlers;
    private readonly IRequestHandler _mvvmHandlers;
    private readonly IRequestHandler _styleHandlers;
    private readonly IRequestHandler _layoutHandlers;
    private readonly IRequestHandler _dependencyPropertyHandlers;

    /// <summary>
    /// Initializes a new aggregated element snapshot handler.
    /// </summary>
    public ElementSnapshotHandlers(
        IRequestHandler treeHandlers,
        IRequestHandler bindingHandlers,
        IRequestHandler mvvmHandlers,
        IRequestHandler styleHandlers,
        IRequestHandler layoutHandlers,
        IRequestHandler dependencyPropertyHandlers)
    {
        _treeHandlers = treeHandlers;
        _bindingHandlers = bindingHandlers;
        _mvvmHandlers = mvvmHandlers;
        _styleHandlers = styleHandlers;
        _layoutHandlers = layoutHandlers;
        _dependencyPropertyHandlers = dependencyPropertyHandlers;
    }

    /// <summary>
    /// Gets the inspector method names supported by this handler.
    /// </summary>
    public IEnumerable<string> GetSupportedMethods() => ["get_element_snapshot"];

    /// <summary>
    /// Handles an aggregated element snapshot request.
    /// </summary>
    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_element_snapshot" => await HandleGetElementSnapshotAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetElementSnapshotAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            throw new ArgumentException("Missing required parameter: elementId");
        }

        var propertyNames = BuildPropertyNames(ParameterHelpers.GetStringArrayParam(@params, "propertyNames"));

        var identityPayload = await ExecuteStepAsync(
            _treeHandlers,
            "get_visual_tree",
            new { elementId, depth = 0, compact = true },
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(identityPayload))
        {
            return identityPayload;
        }

        var dataContextPayload = await ExecuteStepAsync(
            _bindingHandlers,
            "get_datacontext_chain",
            new { elementId },
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(dataContextPayload))
        {
            return dataContextPayload;
        }

        var bindingsPayload = await ExecuteStepAsync(
            _bindingHandlers,
            "get_bindings",
            new { elementId },
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(bindingsPayload))
        {
            return bindingsPayload;
        }

        var validationPayload = await ExecuteStepAsync(
            _mvvmHandlers,
            "get_validation_errors",
            new { elementId },
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(validationPayload))
        {
            return validationPayload;
        }

        var stylePayload = await ExecuteStepAsync(
            _styleHandlers,
            "get_applied_styles",
            new { elementId, compact = true },
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(stylePayload))
        {
            return stylePayload;
        }

        var layoutPayload = await ExecuteStepAsync(
            _layoutHandlers,
            "get_layout_info",
            new { elementId },
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(layoutPayload))
        {
            return layoutPayload;
        }

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propertyName in propertyNames)
        {
            var propertyPayload = await ExecuteStepAsync(
                _dependencyPropertyHandlers,
                "get_dp_value_source",
                new { elementId, propertyName },
                cancellationToken).ConfigureAwait(false);

            if (!IsSuccess(propertyPayload))
            {
                if (string.Equals(GetOptionalString(propertyPayload, "errorCode"), ToolErrorCode.PropertyNotFound.ToString(), StringComparison.Ordinal))
                {
                    continue;
                }

                return propertyPayload;
            }

            properties[propertyName] = BuildPropertySnapshot(propertyPayload);
        }

        var tree = identityPayload.GetProperty("tree");
        return new
        {
            success = true,
            elementId,
            elementType = GetOptionalString(tree, "type"),
            elementName = GetOptionalString(tree, "name"),
            dataContextType = GetDataContextType(dataContextPayload),
            properties,
            bindings = GetArrayOrEmpty(bindingsPayload, "bindings"),
            validationErrors = GetArrayOrEmpty(validationPayload, "errors"),
            style = stylePayload.Clone(),
            layout = layoutPayload.Clone()
        };
    }

    private static IReadOnlyList<string> BuildPropertyNames(string[]? propertyNames)
    {
        var ordered = new List<string>(DefaultPropertyNames.Length + (propertyNames?.Length ?? 0));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var propertyName in DefaultPropertyNames)
        {
            if (seen.Add(propertyName))
            {
                ordered.Add(propertyName);
            }
        }

        if (propertyNames == null)
        {
            return ordered;
        }

        foreach (var propertyName in propertyNames)
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

    private static Dictionary<string, object?> BuildPropertySnapshot(JsonElement propertyPayload)
    {
        var propertySnapshot = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["currentValue"] = GetOptionalString(propertyPayload, "currentValue"),
            ["baseValueSource"] = GetOptionalString(propertyPayload, "baseValueSource")
        };

        if (propertyPayload.TryGetProperty("isExpression", out var isExpressionProperty)
            && isExpressionProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            propertySnapshot["isExpression"] = isExpressionProperty.GetBoolean();
        }

        var localValueKind = GetOptionalString(propertyPayload, "localValueKind");
        if (!string.IsNullOrWhiteSpace(localValueKind))
        {
            propertySnapshot["localValueKind"] = localValueKind;
        }

        return propertySnapshot;
    }

    private static string? GetDataContextType(JsonElement dataContextPayload)
    {
        if (!dataContextPayload.TryGetProperty("chain", out var chain)
            || chain.ValueKind != JsonValueKind.Array
            || chain.GetArrayLength() == 0)
        {
            return null;
        }

        return GetOptionalString(chain[0], "dataContextType");
    }

    private static JsonElement GetArrayOrEmpty(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property)
            ? property.Clone()
            : JsonSerializer.SerializeToElement(Array.Empty<object>());

    private static bool IsSuccess(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("success", out var successProperty)
        && successProperty.ValueKind == JsonValueKind.True;

    private static string? GetOptionalString(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static JsonElement ToJsonElement(object payload) =>
        payload is JsonElement jsonElement
            ? jsonElement.Clone()
            : JsonSerializer.SerializeToElement(payload);

    private static object CreateStepFailure(string method, JsonElement response)
    {
        return new
        {
            success = false,
            failedStep = method,
            error = response.TryGetProperty("error", out var errorProperty)
                ? $"Failed during {method} while building element snapshot. {errorProperty.GetString()}".Trim()
                : $"Failed during {method} while building element snapshot.",
            errorCode = response.TryGetProperty("errorCode", out var errorCodeProperty)
                ? errorCodeProperty.GetString() ?? ToolErrorCode.OperationFailed.ToString()
                : ToolErrorCode.OperationFailed.ToString(),
            hint = response.TryGetProperty("hint", out var hintProperty)
                ? hintProperty.GetString()
                : $"Inspect the failing {method} step and refresh the target element before retrying get_element_snapshot.",
            errorData = response.Clone()
        };
    }

    private static object CreateExceptionStepFailure(string method, string errorCode, string error, string hint)
    {
        return CreateStepFailure(
            method,
            JsonSerializer.SerializeToElement(new
            {
                success = false,
                error,
                errorCode,
                hint
            }));
    }

    private static async Task<JsonElement> ExecuteStepAsync(
        IRequestHandler handler,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(method, JsonSerializer.SerializeToElement(parameters), cancellationToken).ConfigureAwait(false);
            var payload = ToJsonElement(result);
            return IsSuccess(payload)
                ? payload
                : ToJsonElement(CreateStepFailure(method, payload));
        }
        catch (ArgumentException ex)
        {
            return ToJsonElement(CreateExceptionStepFailure(
                method,
                ErrorCode.InvalidParams.ToString(),
                ex.Message,
                $"Inspect the failing {method} step and refresh the target element before retrying get_element_snapshot."));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ToJsonElement(CreateExceptionStepFailure(
                method,
                ErrorCode.InternalError.ToString(),
                "Internal inspector error occurred",
                $"Inspect the failing {method} step and refresh the target element before retrying get_element_snapshot."));
        }
    }
}