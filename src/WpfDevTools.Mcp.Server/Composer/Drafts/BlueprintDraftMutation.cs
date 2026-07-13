using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WpfDevTools.Mcp.Server.Composer.Drafts;

internal static class BlueprintDraftPathMutation
{
    public static BlueprintDraftIssue? Apply(
        JsonObject root,
        string jsonPath,
        JsonElement? value,
        bool remove)
    {
        if (!TryParse(jsonPath, out var segments) || segments.Count == 0)
        {
            return Issue(
                "InvalidBlueprintDraftPath",
                "jsonPath must identify one blueprint property or array item.",
                "Use a path such as $.layout.slots.children[0].properties.text.");
        }

        if (!remove && value is null)
        {
            return Issue(
                "BlueprintDraftValueRequired",
                "Path-based set requires a JSON value.",
                "Pass value, or set remove=true and omit value.");
        }

        if (remove && HasNonNullJsonValue(value))
        {
            return Issue(
                "BlueprintDraftRemoveValueConflict",
                "Path-based remove cannot also provide a JSON value.",
                "Omit value when remove=true.");
        }

        JsonNode current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (!TryGetChild(current, segments[i], out var child))
            {
                return PathNotFound(jsonPath);
            }

            current = child!;
        }

        var final = segments[^1];
        if (final.PropertyName is { } propertyName && current is JsonObject targetObject)
        {
            if (remove)
            {
                return targetObject.Remove(propertyName) ? null : PathNotFound(jsonPath);
            }

            targetObject[propertyName] = ParseValue(value!.Value);
            return null;
        }

        if (final.ArrayIndex is { } arrayIndex && current is JsonArray targetArray)
        {
            if (arrayIndex < 0 || arrayIndex >= targetArray.Count)
            {
                return PathNotFound(jsonPath);
            }

            if (remove)
            {
                targetArray.RemoveAt(arrayIndex);
            }
            else
            {
                targetArray[arrayIndex] = ParseValue(value!.Value);
            }

            return null;
        }

        return PathNotFound(jsonPath);
    }

    private static JsonNode? ParseValue(JsonElement value)
        => JsonNode.Parse(value.GetRawText());

    private static bool HasNonNullJsonValue(JsonElement? value)
        => value is { } element && element.ValueKind != JsonValueKind.Null;

    private static bool TryGetChild(JsonNode current, PathSegment segment, out JsonNode? child)
    {
        child = null;
        if (segment.PropertyName is { } propertyName && current is JsonObject currentObject)
        {
            return currentObject.TryGetPropertyValue(propertyName, out child) && child is not null;
        }

        if (segment.ArrayIndex is { } arrayIndex
            && current is JsonArray currentArray
            && arrayIndex >= 0
            && arrayIndex < currentArray.Count)
        {
            child = currentArray[arrayIndex];
            return child is not null;
        }

        return false;
    }

    private static bool TryParse(string jsonPath, out List<PathSegment> segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(jsonPath) || jsonPath[0] != '$')
        {
            return false;
        }

        var index = 1;
        while (index < jsonPath.Length)
        {
            if (jsonPath[index] == '.')
            {
                var start = ++index;
                while (index < jsonPath.Length && jsonPath[index] is not '.' and not '[')
                {
                    index++;
                }

                var propertyName = jsonPath[start..index];
                if (!IsValidPropertyName(propertyName))
                {
                    return false;
                }

                segments.Add(new PathSegment(propertyName, null));
                continue;
            }

            if (jsonPath[index] == '[')
            {
                index++;
                if (index < jsonPath.Length && jsonPath[index] == '"')
                {
                    var stringStart = index;
                    var escaped = false;
                    var closed = false;
                    for (index++; index < jsonPath.Length; index++)
                    {
                        if (escaped)
                        {
                            escaped = false;
                            continue;
                        }

                        if (jsonPath[index] == '\\')
                        {
                            escaped = true;
                            continue;
                        }

                        if (jsonPath[index] == '"')
                        {
                            index++;
                            closed = true;
                            break;
                        }
                    }

                    if (!closed || index >= jsonPath.Length || jsonPath[index] != ']')
                    {
                        return false;
                    }

                    try
                    {
                        var propertyName = JsonSerializer.Deserialize<string>(jsonPath[stringStart..index]);
                        if (propertyName is null)
                        {
                            return false;
                        }

                        segments.Add(new PathSegment(propertyName, null));
                    }
                    catch (JsonException)
                    {
                        return false;
                    }

                    index++;
                    continue;
                }

                var start = index;
                while (index < jsonPath.Length && char.IsAsciiDigit(jsonPath[index]))
                {
                    index++;
                }

                if (start == index
                    || index >= jsonPath.Length
                    || jsonPath[index] != ']'
                    || !int.TryParse(
                        jsonPath[start..index],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var arrayIndex))
                {
                    return false;
                }

                index++;
                segments.Add(new PathSegment(null, arrayIndex));
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsValidPropertyName(string value)
        => value.Length > 0
           && (char.IsAsciiLetter(value[0]) || value[0] == '_')
           && value.Skip(1).All(character =>
               char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static BlueprintDraftIssue PathNotFound(string jsonPath)
        => Issue(
            "BlueprintDraftPathNotFound",
            $"Blueprint draft path '{jsonPath}' does not identify an existing parent and target.",
            "Use a jsonPath from the current blueprint structure; object leaf properties may be added under an existing object.");

    private static BlueprintDraftIssue Issue(string code, string message, string repairSuggestion)
        => new(code, message, repairSuggestion);

    private sealed record PathSegment(string? PropertyName, int? ArrayIndex);
}

internal static class BlueprintDraftChangeSummaryBuilder
{
    private const int MaxReportedChanges = 64;
    private const int MaxScalarCharacters = 160;

    public static BlueprintDraftChangeSummary Build(JsonNode? before, JsonNode? after)
    {
        var changes = new List<BlueprintDraftChange>();
        var changeCount = 0;
        Collect(before, after, "$", beforeExists: true, afterExists: true, changes, ref changeCount);
        return new BlueprintDraftChangeSummary(
            changeCount,
            changes.Count,
            changeCount > changes.Count,
            changes);
    }

    private static void Collect(
        JsonNode? before,
        JsonNode? after,
        string jsonPath,
        bool beforeExists,
        bool afterExists,
        List<BlueprintDraftChange> changes,
        ref int changeCount)
    {
        if (beforeExists == afterExists && JsonNode.DeepEquals(before, after))
        {
            return;
        }

        if (!beforeExists || !afterExists)
        {
            AddChange(before, after, jsonPath, beforeExists, afterExists, changes, ref changeCount);
            return;
        }

        if (before is JsonObject beforeObject && after is JsonObject afterObject)
        {
            foreach (var name in beforeObject.Select(item => item.Key)
                         .Union(afterObject.Select(item => item.Key), StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                var hasBefore = beforeObject.TryGetPropertyValue(name, out var beforeValue);
                var hasAfter = afterObject.TryGetPropertyValue(name, out var afterValue);
                Collect(
                    beforeValue,
                    afterValue,
                    AppendPropertyPath(jsonPath, name),
                    hasBefore,
                    hasAfter,
                    changes,
                    ref changeCount);
            }

            return;
        }

        if (before is JsonArray beforeArray
            && after is JsonArray afterArray
            && beforeArray.Count == afterArray.Count)
        {
            for (var index = 0; index < beforeArray.Count; index++)
            {
                Collect(
                    beforeArray[index],
                    afterArray[index],
                    $"{jsonPath}[{index}]",
                    beforeExists: true,
                    afterExists: true,
                    changes,
                    ref changeCount);
            }

            return;
        }

        AddChange(before, after, jsonPath, beforeExists, afterExists, changes, ref changeCount);
    }

    private static void AddChange(
        JsonNode? before,
        JsonNode? after,
        string jsonPath,
        bool beforeExists,
        bool afterExists,
        List<BlueprintDraftChange> changes,
        ref int changeCount)
    {
        changeCount++;
        if (changes.Count >= MaxReportedChanges)
        {
            return;
        }

        changes.Add(new BlueprintDraftChange(
            jsonPath,
            !beforeExists ? "added" : !afterExists ? "removed" : "modified",
            Describe(before, beforeExists),
            Describe(after, afterExists)));
    }

    private static string? Describe(JsonNode? value, bool exists)
    {
        if (!exists)
        {
            return null;
        }

        if (value is null)
        {
            return "null";
        }

        if (value is JsonObject jsonObject)
        {
            return $"object({jsonObject.Count})";
        }

        if (value is JsonArray jsonArray)
        {
            return $"array({jsonArray.Count})";
        }

        var text = value.ToJsonString();
        return text.Length <= MaxScalarCharacters
            ? text
            : text[..MaxScalarCharacters] + "...";
    }

    private static string AppendPropertyPath(string parentPath, string propertyName)
        => IsSimplePropertyName(propertyName)
            ? parentPath + "." + propertyName
            : parentPath + "[" + JsonSerializer.Serialize(propertyName) + "]";

    private static bool IsSimplePropertyName(string value)
        => value.Length > 0
           && (char.IsAsciiLetter(value[0]) || value[0] == '_')
           && value.Skip(1).All(character =>
               char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
