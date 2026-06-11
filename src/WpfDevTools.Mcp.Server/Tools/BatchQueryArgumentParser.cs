using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed record BatchTargetParseResult(IReadOnlyList<string?> Targets, bool IsBatch, object? Error);

public static class BatchQueryArgumentParser
{
    public static BatchTargetParseResult ParseElementTargets(
        JsonElement? arguments,
        string singleName,
        string pluralName)
    {
        return ParseStringTargets(arguments, singleName, pluralName, requireAtLeastOne: false, defaultTarget: null);
    }

    public static BatchTargetParseResult ParseStringTargets(
        JsonElement? arguments,
        string singleName,
        string pluralName,
        bool requireAtLeastOne,
        string? defaultTarget = null)
    {
        var singleValue = TryGetSingle(arguments, singleName, out var singleError);
        if (singleError != null)
        {
            return new BatchTargetParseResult(Array.Empty<string?>(), false, singleError);
        }

        var pluralValues = TryGetMany(arguments, pluralName, out var pluralError);
        if (pluralError != null)
        {
            return new BatchTargetParseResult(Array.Empty<string?>(), false, pluralError);
        }

        if (singleValue != null && pluralValues != null)
        {
            return new BatchTargetParseResult(Array.Empty<string?>(), false, CreateInvalidArgument(
                $"Provide either {singleName} or {pluralName}, not both."));
        }

        if (pluralValues != null)
        {
            return new BatchTargetParseResult(pluralValues, pluralValues.Count > 1, null);
        }

        if (singleValue != null)
        {
            return new BatchTargetParseResult(new[] { singleValue }, false, null);
        }

        if (requireAtLeastOne)
        {
            return new BatchTargetParseResult(Array.Empty<string?>(), false, CreateInvalidArgument(
                $"Provide {singleName} or {pluralName}."));
        }

        return new BatchTargetParseResult(new string?[] { defaultTarget }, false, null);
    }

    private static string? TryGetSingle(JsonElement? arguments, string name, out object? error)
    {
        error = null;
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = CreateInvalidArgument($"{name} must be a string when provided.");
            return null;
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = CreateInvalidArgument($"{name} must be a non-empty string when provided.");
            return null;
        }

        return value;
    }

    private static IReadOnlyList<string?>? TryGetMany(JsonElement? arguments, string name, out object? error)
    {
        error = null;
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = CreateInvalidArgument($"{name} must be an array of strings when provided.");
            return null;
        }

        var itemCount = property.GetArrayLength();
        if (itemCount > BatchItemLimits.MaxQueryInputItems)
        {
            error = BatchItemLimits.CreateInvalidArgumentError(
                name,
                itemCount,
                BatchItemLimits.MaxQueryInputItems,
                $"{name} must contain at most {BatchItemLimits.MaxQueryInputItems} items; received {itemCount}.",
                $"Split large batch queries into smaller batches of {BatchItemLimits.MaxQueryInputItems} or fewer values.");
            return null;
        }

        var values = new List<string?>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = CreateInvalidArgument($"{name} must contain only strings.");
                return null;
            }

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                error = CreateInvalidArgument($"{name} must not contain empty strings.");
                return null;
            }

            values.Add(value);
        }

        if (values.Count == 0)
        {
            error = CreateInvalidArgument($"{name} must not be empty.");
            return null;
        }

        return values;
    }

    private static object CreateInvalidArgument(string message) =>
        new ToolErrorPayload
        {
            Error = message,
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "Use either the singular input or the plural batch input, and make sure each value is a non-empty string."
        };
}

public static class BatchQueryExecutor
{
    public enum CombinationMode
    {
        CrossProduct,
        PairwiseOrBroadcast
    }

    public static async Task<object> ExecuteAsync(
        IReadOnlyList<string?> elementIds,
        IReadOnlyList<string?> propertyNames,
        Func<string?, string?, CancellationToken, Task<object>> queryAsync,
        CancellationToken cancellationToken,
        CombinationMode combinationMode = CombinationMode.CrossProduct)
    {
        var results = new List<(JsonElement element, string? elementId, string? propertyName)>();
        var targets = BuildTargets(elementIds, propertyNames, combinationMode);
        if (targets.Error != null)
        {
            return targets.Error;
        }

        foreach (var (elementId, propertyName) in targets.Pairs)
        {
            var result = await queryAsync(elementId, propertyName, cancellationToken).ConfigureAwait(false);
            var element = result is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(result);

            results.Add((element, elementId, propertyName));
        }

        if (results.Count == 1)
        {
            return results[0].element;
        }

        var combinations = results
            .Select(static item => AttachCorrelation(item.element, item.elementId, item.propertyName))
            .ToArray();

        var successCount = combinations.Count(static item =>
            !item.TryGetProperty("success", out var success) || success.GetBoolean());
        var failureCount = combinations.Length - successCount;

        return JsonSerializer.SerializeToElement(new
        {
            success = successCount > 0,
            resultCount = combinations.Length,
            successCount,
            failureCount,
            results = combinations
        });
    }

    private static (IReadOnlyList<(string? elementId, string? propertyName)> Pairs, object? Error) BuildTargets(
        IReadOnlyList<string?> elementIds,
        IReadOnlyList<string?> propertyNames,
        CombinationMode combinationMode)
    {
        if (combinationMode == CombinationMode.CrossProduct)
        {
            var pairCount = (long)elementIds.Count * propertyNames.Count;
            if (pairCount > BatchItemLimits.MaxQueryExpandedItems)
            {
                return (Array.Empty<(string? elementId, string? propertyName)>(), CreateExpansionLimitExceeded(
                    "crossProduct",
                    pairCount));
            }

            var pairs = new List<(string? elementId, string? propertyName)>((int)pairCount);
            foreach (var elementId in elementIds)
            {
                foreach (var propertyName in propertyNames)
                {
                    pairs.Add((elementId, propertyName));
                }
            }

            return (pairs, null);
        }

        var expandedItemCount = Math.Max(elementIds.Count, propertyNames.Count);
        if (expandedItemCount > BatchItemLimits.MaxQueryExpandedItems)
        {
            return (Array.Empty<(string? elementId, string? propertyName)>(), CreateExpansionLimitExceeded(
                "pairwiseBroadcast",
                expandedItemCount));
        }

        if (elementIds.Count > 1 && propertyNames.Count > 1 && elementIds.Count != propertyNames.Count)
        {
            return (Array.Empty<(string? elementId, string? propertyName)>(), CreateInvalidArgument(
                "When both elementIds and propertyNames contain multiple values, they must have the same length for pairwise correlation."));
        }

        if (elementIds.Count == propertyNames.Count)
        {
            return (elementIds.Zip(propertyNames, static (elementId, propertyName) => (elementId, propertyName)).ToArray(), null);
        }

        if (elementIds.Count == 1)
        {
            return (propertyNames.Select(propertyName => (elementIds[0], propertyName)).ToArray(), null);
        }

        return (elementIds.Select(elementId => (elementId, propertyNames[0])).ToArray(), null);
    }

    private static JsonElement AttachCorrelation(JsonElement result, string? elementId, string? propertyName)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            return JsonSerializer.SerializeToElement(new { success = false, elementId, propertyName, result });
        }

        var payload = new Dictionary<string, object?>();
        foreach (var property in result.EnumerateObject())
        {
            payload[property.Name] = property.Value.Clone();
        }

        if (elementId != null)
        {
            payload["elementId"] = elementId;
        }

        if (propertyName != null)
        {
            payload["propertyName"] = propertyName;
        }

        return JsonSerializer.SerializeToElement(payload);
    }

    private static object CreateInvalidArgument(string message) =>
        new ToolErrorPayload
        {
            Error = message,
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "Use equal-length elementIds/propertyNames for pairwise batch inspection, or provide a single elementId/propertyName to broadcast across the other batch axis."
        };

    private static object CreateExpansionLimitExceeded(string mode, long actualItems)
    {
        var displayMode = mode switch
        {
            "crossProduct" => "cross-product",
            "pairwiseBroadcast" => "pairwise-broadcast",
            _ => mode
        };

        return BatchItemLimits.CreateInvalidArgumentError(
            "queryExpansion",
            actualItems,
            BatchItemLimits.MaxQueryExpandedItems,
            $"Batch query {displayMode} expansion would produce {actualItems} items; maximum is {BatchItemLimits.MaxQueryExpandedItems}.",
            $"Split large batch queries into smaller batches so each request produces {BatchItemLimits.MaxQueryExpandedItems} or fewer results.",
            new Dictionary<string, object?> { ["mode"] = mode });
    }
}
