using System.Globalization;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintCompositionTargetPath
{
    private const string RootPath = "$.layout";
    private const string SlotMarker = ".slots";

    public static string AppendProperty(string parentPath, string propertyName)
        => IsSimpleSegment(propertyName)
            ? $"{parentPath}.{propertyName}"
            : $"{parentPath}[{JsonSerializer.Serialize(propertyName)}]";

    public static bool TryParse(string path, out IReadOnlyList<BlueprintCompositionPathSegment> segments)
    {
        var parsed = new List<BlueprintCompositionPathSegment>();
        segments = parsed;
        if (!path.StartsWith(RootPath, StringComparison.Ordinal))
        {
            return false;
        }

        var offset = RootPath.Length;
        while (offset < path.Length)
        {
            if (!path.AsSpan(offset).StartsWith(SlotMarker, StringComparison.Ordinal))
            {
                return false;
            }

            offset += SlotMarker.Length;
            if (!TryReadProperty(path, ref offset, out var slotName))
            {
                return false;
            }

            int? childIndex = null;
            if (offset < path.Length && path[offset] == '[')
            {
                if (!TryReadIndex(path, ref offset, out var index))
                {
                    return false;
                }

                childIndex = index;
            }

            parsed.Add(new BlueprintCompositionPathSegment(slotName, childIndex));
        }

        return parsed.Count > 0;
    }

    private static bool TryReadProperty(string path, ref int offset, out string propertyName)
    {
        propertyName = string.Empty;
        if (offset >= path.Length)
        {
            return false;
        }

        if (path[offset] == '.')
        {
            var start = ++offset;
            while (offset < path.Length && IsSimpleSegmentCharacter(path[offset]))
            {
                offset++;
            }

            propertyName = path[start..offset];
            return IsSimpleSegment(propertyName);
        }

        if (path[offset] != '[' || offset + 1 >= path.Length || path[offset + 1] != '"')
        {
            return false;
        }

        var stringStart = offset + 1;
        var cursor = stringStart + 1;
        var escaped = false;
        while (cursor < path.Length)
        {
            var character = path[cursor];
            if (!escaped && character == '"')
            {
                break;
            }

            escaped = !escaped && character == '\\';
            if (character != '\\')
            {
                escaped = false;
            }

            cursor++;
        }

        if (cursor >= path.Length || cursor + 1 >= path.Length || path[cursor + 1] != ']')
        {
            return false;
        }

        try
        {
            propertyName = JsonSerializer.Deserialize<string>(path[stringStart..(cursor + 1)]) ?? string.Empty;
        }
        catch (JsonException)
        {
            return false;
        }

        offset = cursor + 2;
        return propertyName.Length > 0;
    }

    private static bool TryReadIndex(string path, ref int offset, out int index)
    {
        index = 0;
        var start = ++offset;
        while (offset < path.Length && char.IsAsciiDigit(path[offset]))
        {
            offset++;
        }

        if (start == offset || offset >= path.Length || path[offset] != ']'
            || !int.TryParse(path.AsSpan(start, offset - start), NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        offset++;
        return true;
    }

    private static bool IsSimpleSegment(string value)
        => value.Length > 0
           && (char.IsAsciiLetter(value[0]) || value[0] == '_')
           && value.All(IsSimpleSegmentCharacter);

    private static bool IsSimpleSegmentCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';
}

internal sealed record BlueprintCompositionPathSegment(string SlotName, int? ChildIndex);
