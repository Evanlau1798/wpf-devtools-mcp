using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Inspector.Host.Handlers;

internal interface IElementSnapshotAggregator
{
    Task<object> GetElementSnapshotAsync(JsonElement? @params, CancellationToken cancellationToken);
}

internal delegate Task<object> ElementSnapshotStepExecutor(
    string method,
    string elementId,
    string? propertyName,
    CancellationToken cancellationToken);

internal sealed class ElementSnapshotAggregator : IElementSnapshotAggregator
{
    private static readonly string[] DefaultPropertyNames = ["Text", "Content", "Visibility", "IsEnabled", "Opacity"];
    private readonly ElementSnapshotStepExecutor _executeStep;

    public ElementSnapshotAggregator(
        VisualTreeAnalyzer visualTreeAnalyzer,
        BindingAnalyzer bindingAnalyzer,
        MvvmAnalyzer mvvmAnalyzer,
        StyleAnalyzer styleAnalyzer,
        LayoutAnalyzer layoutAnalyzer,
        DependencyPropertyAnalyzer dependencyPropertyAnalyzer)
        : this(CreateAnalyzerExecutor(
            visualTreeAnalyzer,
            bindingAnalyzer,
            mvvmAnalyzer,
            styleAnalyzer,
            layoutAnalyzer,
            dependencyPropertyAnalyzer))
    {
    }

    internal ElementSnapshotAggregator(ElementSnapshotStepExecutor executeStep)
    {
        _executeStep = executeStep;
    }

    public async Task<object> GetElementSnapshotAsync(
        JsonElement? @params,
        CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            throw new ArgumentException("Missing required parameter: elementId");
        }

        var propertyNames = BuildPropertyNames(ParameterHelpers.GetStringArrayParam(@params, "propertyNames"));

        var identityPayload = await ExecuteStepAsync(
            "get_visual_tree",
            elementId,
            propertyName: null,
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(identityPayload))
        {
            return identityPayload;
        }

        var dataContextPayload = await ExecuteStepAsync(
            "get_datacontext_chain",
            elementId,
            propertyName: null,
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(dataContextPayload))
        {
            return dataContextPayload;
        }

        var bindingsPayload = await ExecuteStepAsync(
            "get_bindings",
            elementId,
            propertyName: null,
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(bindingsPayload))
        {
            return bindingsPayload;
        }

        var validationPayload = await ExecuteStepAsync(
            "get_validation_errors",
            elementId,
            propertyName: null,
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(validationPayload))
        {
            return validationPayload;
        }

        var stylePayload = await ExecuteStepAsync(
            "get_applied_styles",
            elementId,
            propertyName: null,
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(stylePayload))
        {
            return stylePayload;
        }

        var layoutPayload = await ExecuteStepAsync(
            "get_layout_info",
            elementId,
            propertyName: null,
            cancellationToken).ConfigureAwait(false);
        if (!IsSuccess(layoutPayload))
        {
            return layoutPayload;
        }

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propertyName in propertyNames)
        {
            var propertyPayload = await ExecuteStepAsync(
                "get_dp_value_source",
                elementId,
                propertyName,
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

    private static ElementSnapshotStepExecutor CreateAnalyzerExecutor(
        VisualTreeAnalyzer visualTreeAnalyzer,
        BindingAnalyzer bindingAnalyzer,
        MvvmAnalyzer mvvmAnalyzer,
        StyleAnalyzer styleAnalyzer,
        LayoutAnalyzer layoutAnalyzer,
        DependencyPropertyAnalyzer dependencyPropertyAnalyzer)
    {
        return (method, elementId, propertyName, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = method switch
            {
                "get_visual_tree" => visualTreeAnalyzer.GetVisualTreeWithOptions(
                    TreeTraversalOptions.Create(0, compact: true, summaryOnly: null, maxNodes: null, maxChildrenPerNode: null),
                    elementId),
                "get_datacontext_chain" => bindingAnalyzer.GetDataContextChain(elementId),
                "get_bindings" => bindingAnalyzer.GetBindings(elementId),
                "get_validation_errors" => mvvmAnalyzer.GetValidationErrors(elementId),
                "get_applied_styles" => styleAnalyzer.GetAppliedStyles(elementId, compact: true),
                "get_layout_info" => layoutAnalyzer.GetLayoutInfo(elementId),
                "get_dp_value_source" => dependencyPropertyAnalyzer.GetValueSource(propertyName!, elementId),
                _ => throw new InvalidOperationException($"Unsupported element snapshot step: {method}")
            };

            return Task.FromResult(result);
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

    private async Task<JsonElement> ExecuteStepAsync(
        string method,
        string elementId,
        string? propertyName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _executeStep(method, elementId, propertyName, cancellationToken).ConfigureAwait(false);
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
