namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static IReadOnlyList<RenderSourceMapEntry> ResolveSourceMap(
        string xaml,
        IReadOnlyList<RenderSourceMapEntry> entries)
    {
        var resolved = new List<RenderSourceMapEntry>();
        var siblingOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = entries
            .Select((entry, index) => (Entry: entry, Index: index))
            .OrderBy(item => GetNodeDepth(item.Entry.JsonPath))
            .ThenBy(item => item.Index)
            .Select(item => item.Entry);
        foreach (var entry in ordered)
        {
            var parent = resolved
                .Where(candidate => IsChildPath(entry.JsonPath, candidate.JsonPath))
                .OrderByDescending(candidate => candidate.JsonPath.Length)
                .FirstOrDefault();
            var start = parent is null ? 0 : parent.StartIndex;
            if (parent is not null && siblingOffsets.TryGetValue(parent.JsonPath, out var siblingOffset))
            {
                start = Math.Max(start, siblingOffset);
            }

            var limit = parent is null ? xaml.Length : parent.EndIndex;
            var index = FindSpanStart(xaml, entry.Xaml, start, Math.Max(0, limit - start));
            if (index < 0)
            {
                continue;
            }

            var end = index + entry.Xaml.Length;
            var startPosition = ToLineColumn(xaml, index);
            var endPosition = ToLineColumn(xaml, Math.Max(index, end - 1));
            resolved.Add(entry with
            {
                StartIndex = index,
                EndIndex = end,
                StartLine = startPosition.Line,
                StartColumn = startPosition.Column,
                EndLine = endPosition.Line,
                EndColumn = endPosition.Column
            });
            if (parent is not null)
            {
                siblingOffsets[parent.JsonPath] = end;
            }
        }

        return resolved.OrderBy(entry => entry.StartIndex).ThenBy(entry => entry.JsonPath.Length).ToArray();
    }

    private static int FindSpanStart(string haystack, string needle, int startIndex, int length)
    {
        if (string.IsNullOrEmpty(needle) || startIndex < 0 || startIndex >= haystack.Length)
        {
            return -1;
        }

        return haystack.IndexOf(needle, startIndex, Math.Min(length, haystack.Length - startIndex), StringComparison.Ordinal);
    }

    private static bool IsChildPath(string path, string parentPath)
        => path.Length > parentPath.Length
            && path.StartsWith(parentPath + ".slots.", StringComparison.Ordinal);

    private static int GetNodeDepth(string jsonPath)
    {
        var depth = 0;
        for (var index = 0; (index = jsonPath.IndexOf(".slots.", index, StringComparison.Ordinal)) >= 0; index += 7)
        {
            depth++;
        }
        return depth;
    }

    private static (int Line, int Column) ToLineColumn(string text, int index)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                line++;
                column = 1;
                if (i + 1 < index && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }
}
