using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintJsonShapeIssueFactory
{
    private const string NodeShape = "{ \"kind\": \"pack.block\", \"slots\": {} }";
    private const string SlotMapShape = "{ \"slotName\": [{ \"kind\": \"pack.block\" }] }";
    private const string SlotItemsShape = "[{ \"kind\": \"pack.block\" }]";
    private const string PackListShape = "[{ \"id\": \"pack-id\", \"version\": \"1.0.0\", \"role\": \"primary\", \"required\": true }]";

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
            var expected = DescribeExpectedShape(path);
            issue = new BlueprintValidationIssue(
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
            return true;
        }
    }

    private static string DescribeExpectedShape(string path)
    {
        var segments = ParseSegments(path).ToArray();
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
            "slots" => SlotMapShape,
            _ when previous == "slots" => SlotItemsShape,
            "properties" or "bindings" or "metadata" or "resourcevariants" => "{}",
            "required" => "true",
            "kind" or "elementname" or "automationid" or "name" or "id" or "version" or "role" => "\"text\"",
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

    private readonly record struct PathSegment(string? PropertyName, int? ArrayIndex);
}
