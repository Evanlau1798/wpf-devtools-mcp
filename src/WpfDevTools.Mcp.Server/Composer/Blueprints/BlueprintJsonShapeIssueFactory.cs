using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintJsonShapeIssueFactory
{
    private const string BlueprintShape = "{ \"schemaVersion\": \"wpfdevtools.ui-blueprint.v1\", \"name\": \"BlueprintName\", \"packs\": [{ \"id\": \"pack-id\", \"version\": \"1.0.0\", \"role\": \"primary\", \"required\": true }], \"primaryPack\": \"pack-id\", \"layout\": { \"kind\": \"pack.block\" } }";
    private const string NodeShape = "{ \"kind\": \"pack.block\", \"slots\": {} }";
    private const string SlotMapShape = "{ \"slotName\": [{ \"kind\": \"pack.block\" }] }";
    private const string SlotItemsShape = "[{ \"kind\": \"pack.block\" }]";
    private const string PackReferenceShape = "{ \"id\": \"pack-id\", \"version\": \"1.0.0\", \"role\": \"primary\", \"required\": true }";
    private const string PackListShape = "[" + PackReferenceShape + "]";
    private static readonly string[] BlueprintStringFields = ["schemaVersion", "name", "primaryPack"];
    private static readonly string[] PackStringFields = ["id", "version", "role"];
    private static readonly string[] NodeStringFields = ["kind", "elementName", "automationId"];
    private static readonly string[] NodeMapFields = ["properties", "bindings", "metadata"];

    public static bool TryCreateStructuralMismatch(
        string blueprintJson,
        out BlueprintValidationIssue issue)
    {
        issue = null!;
        try
        {
            using var document = JsonDocument.Parse(blueprintJson);
            return TryFindStructuralMismatch(document.RootElement, out issue);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryCreate(
        string blueprintJson,
        JsonException exception,
        out BlueprintValidationIssue issue)
    {
        issue = null!;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(blueprintJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var path = string.IsNullOrWhiteSpace(exception.Path) ? "$" : exception.Path!;
            var observed = TryResolve(document.RootElement, path, out var value)
                ? value.ValueKind.ToString()
                : "Unknown";
            issue = CreateIssue(path, observed);
            return true;
        }
    }

    private static bool TryFindStructuralMismatch(
        JsonElement root,
        out BlueprintValidationIssue issue)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            issue = CreateIssue("$", root.ValueKind.ToString());
            return true;
        }

        foreach (var propertyName in BlueprintStringFields)
        {
            if (TryFindPropertyKindMismatch(root, propertyName, "$", JsonValueKind.String, out issue))
            {
                return true;
            }
        }

        if (TryFindPacksMismatch(root, out issue)
            || TryFindStringMapMismatch(root, "resourceVariants", "$", out issue)
            || TryFindObjectPropertyMismatch(root, "metadata", "$", out issue))
        {
            return true;
        }

        if (!root.TryGetProperty("layout", out var layout))
        {
            issue = null!;
            return false;
        }

        if (layout.ValueKind != JsonValueKind.Object)
        {
            issue = CreateIssue("$.layout", layout.ValueKind.ToString());
            return true;
        }

        return TryFindNodeMismatch(layout, "$.layout", out issue);
    }

    private static bool TryFindPacksMismatch(JsonElement root, out BlueprintValidationIssue issue)
    {
        if (!root.TryGetProperty("packs", out var packs))
        {
            issue = null!;
            return false;
        }

        if (packs.ValueKind != JsonValueKind.Array)
        {
            issue = CreateIssue("$.packs", packs.ValueKind.ToString());
            return true;
        }

        for (var index = 0; index < packs.GetArrayLength(); index++)
        {
            var pack = packs[index];
            var path = $"$.packs[{index}]";
            if (pack.ValueKind != JsonValueKind.Object)
            {
                issue = CreateIssue(path, pack.ValueKind.ToString());
                return true;
            }

            foreach (var propertyName in PackStringFields)
            {
                if (TryFindPropertyKindMismatch(pack, propertyName, path, JsonValueKind.String, out issue))
                {
                    return true;
                }
            }

            if (TryFindPropertyKindMismatch(pack, "required", path, JsonValueKind.True, out issue, JsonValueKind.False))
            {
                return true;
            }
        }

        issue = null!;
        return false;
    }

    private static bool TryFindNodeMismatch(
        JsonElement node,
        string path,
        out BlueprintValidationIssue issue)
    {
        foreach (var propertyName in NodeStringFields)
        {
            if (TryFindPropertyKindMismatch(
                node,
                propertyName,
                path,
                JsonValueKind.String,
                out issue,
                propertyName == "kind" ? null : JsonValueKind.Null))
            {
                return true;
            }
        }

        foreach (var propertyName in NodeMapFields)
        {
            if (TryFindObjectPropertyMismatch(node, propertyName, path, out issue))
            {
                return true;
            }
        }

        if (!node.TryGetProperty("slots", out var slots))
        {
            issue = null!;
            return false;
        }

        if (slots.ValueKind != JsonValueKind.Object)
        {
            issue = CreateIssue(path + ".slots", slots.ValueKind.ToString());
            return true;
        }

        foreach (var slot in slots.EnumerateObject())
        {
            var slotPath = AppendPropertyPath(path + ".slots", slot.Name);
            if (slot.Value.ValueKind != JsonValueKind.Array)
            {
                issue = CreateIssue(slotPath, slot.Value.ValueKind.ToString(), SlotItemsShape);
                return true;
            }

            for (var index = 0; index < slot.Value.GetArrayLength(); index++)
            {
                var child = slot.Value[index];
                var childPath = $"{slotPath}[{index}]";
                if (child.ValueKind != JsonValueKind.Object)
                {
                    issue = CreateIssue(childPath, child.ValueKind.ToString(), NodeShape);
                    return true;
                }

                if (TryFindNodeMismatch(child, childPath, out issue))
                {
                    return true;
                }
            }
        }

        issue = null!;
        return false;
    }

    private static bool TryFindStringMapMismatch(
        JsonElement parent,
        string propertyName,
        string parentPath,
        out BlueprintValidationIssue issue)
    {
        if (TryFindObjectPropertyMismatch(parent, propertyName, parentPath, out issue))
        {
            return true;
        }

        if (!parent.TryGetProperty(propertyName, out var map))
        {
            return false;
        }

        foreach (var property in map.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                issue = CreateIssue(
                    AppendPropertyPath($"{parentPath}.{propertyName}", property.Name),
                    property.Value.ValueKind.ToString(),
                    "\"text\"");
                return true;
            }
        }

        return false;
    }

    private static bool TryFindObjectPropertyMismatch(
        JsonElement parent,
        string propertyName,
        string parentPath,
        out BlueprintValidationIssue issue)
        => TryFindPropertyKindMismatch(
            parent,
            propertyName,
            parentPath,
            JsonValueKind.Object,
            out issue);

    private static bool TryFindPropertyKindMismatch(
        JsonElement parent,
        string propertyName,
        string parentPath,
        JsonValueKind expected,
        out BlueprintValidationIssue issue,
        JsonValueKind? alternate = null)
    {
        if (!parent.TryGetProperty(propertyName, out var property)
            || property.ValueKind == expected
            || property.ValueKind == alternate)
        {
            issue = null!;
            return false;
        }

        issue = CreateIssue($"{parentPath}.{propertyName}", property.ValueKind.ToString());
        return true;
    }

    private static BlueprintValidationIssue CreateIssue(
        string path,
        string observed,
        string? expectedShape = null)
    {
        var expected = expectedShape ?? DescribeExpectedShape(path);
        return new BlueprintValidationIssue(
            path,
            "InvalidBlueprintShape",
            $"Blueprint value at '{path}' is {observed}; its JSON shape does not match the blueprint contract.",
            $"Replace {path} with {expected}.",
            [],
            [],
            null)
        {
            ObservedValueKind = observed,
            ExpectedJsonShape = expected
        };
    }

    private static string DescribeExpectedShape(string path)
    {
        var segments = ParseSegments(path).ToArray();
        if (segments.Length == 0)
        {
            return BlueprintShape;
        }

        var propertyNames = segments
            .Where(segment => segment.PropertyName is not null)
            .Select(segment => segment.PropertyName!.ToLowerInvariant())
            .ToArray();
        if (segments[^1].ArrayIndex is not null)
        {
            if (propertyNames.LastOrDefault() == "packs")
            {
                return PackReferenceShape;
            }

            if (propertyNames.Reverse().Skip(1).FirstOrDefault() == "slots")
            {
                return NodeShape;
            }
        }

        var last = segments.LastOrDefault(segment => segment.PropertyName is not null)
            .PropertyName?.ToLowerInvariant();
        var previous = segments
            .Where(segment => segment.PropertyName is not null)
            .Select(segment => segment.PropertyName)
            .Reverse()
            .Skip(1)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        return last switch
        {
            "layout" => NodeShape,
            "packs" => PackListShape,
            "schemaversion" => "\"wpfdevtools.ui-blueprint.v1\"",
            "slots" => SlotMapShape,
            _ when previous == "slots" => SlotItemsShape,
            "properties" or "bindings" or "metadata" or "resourcevariants" => "{}",
            "required" => "true",
            _ when previous == "resourcevariants" => "\"text\"",
            "kind" or "elementname" or "automationid" or "name" or "primarypack" or "id" or "version" or "role" => "\"text\"",
            _ => "a JSON value matching the documented blueprint field shape"
        };
    }

    private static bool TryResolve(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in ParseSegments(path))
        {
            if (segment.PropertyName is string propertyName)
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out value))
                {
                    return false;
                }
            }
            else if (segment.ArrayIndex is int index)
            {
                if (value.ValueKind != JsonValueKind.Array || index < 0 || index >= value.GetArrayLength())
                {
                    return false;
                }

                value = value[index];
            }
        }

        return true;
    }

    private static string AppendPropertyPath(string parentPath, string propertyName)
        => IsSimplePropertyName(propertyName)
            ? $"{parentPath}.{propertyName}"
            : $"{parentPath}[{JsonSerializer.Serialize(propertyName)}]";

    private static bool IsSimplePropertyName(string propertyName)
        => propertyName.Length > 0
           && IsAsciiLetterOrUnderscore(propertyName[0])
           && propertyName.Skip(1).All(character =>
               IsAsciiLetterOrUnderscore(character) || char.IsAsciiDigit(character));

    private static bool IsAsciiLetterOrUnderscore(char character)
        => character == '_'
           || character is >= 'a' and <= 'z'
           || character is >= 'A' and <= 'Z';

    private static IEnumerable<PathSegment> ParseSegments(string path)
    {
        for (var index = path.StartsWith('$') ? 1 : 0; index < path.Length;)
        {
            if (path[index] == '.')
            {
                var start = ++index;
                while (index < path.Length && path[index] is not '.' and not '[')
                {
                    index++;
                }

                if (index > start)
                {
                    yield return new(path[start..index], null);
                }

                continue;
            }

            if (path[index] == '[')
            {
                if (TryReadQuotedProperty(path, index, out var propertyName, out var nextIndex))
                {
                    yield return new(propertyName, null);
                    index = nextIndex;
                    continue;
                }

                var close = path.IndexOf(']', index + 1);
                if (close < 0 || !int.TryParse(path[(index + 1)..close], out var arrayIndex))
                {
                    yield break;
                }

                yield return new(null, arrayIndex);
                index = close + 1;
                continue;
            }

            yield break;
        }
    }

    private static bool TryReadQuotedProperty(
        string path,
        int openBracketIndex,
        out string propertyName,
        out int nextIndex)
    {
        propertyName = string.Empty;
        nextIndex = openBracketIndex;
        var quoteStart = openBracketIndex + 1;
        if (quoteStart >= path.Length || path[quoteStart] != '"')
        {
            return false;
        }

        var escaped = false;
        for (var index = quoteStart + 1; index < path.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (path[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (path[index] != '"' || index + 1 >= path.Length || path[index + 1] != ']')
            {
                continue;
            }

            try
            {
                propertyName = JsonSerializer.Deserialize<string>(path[quoteStart..(index + 1)]) ?? string.Empty;
            }
            catch (JsonException)
            {
                return false;
            }

            nextIndex = index + 2;
            return true;
        }

        return false;
    }

    private readonly record struct PathSegment(string? PropertyName, int? ArrayIndex);
}
