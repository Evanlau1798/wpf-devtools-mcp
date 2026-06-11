using System.Text.Json;

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

        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestWithoutPiggybackAsync(
            processId,
            "get_element_snapshot",
            new
            {
                elementId,
                propertyNames = BuildSnapshotPropertyNames(ParseStringArrayParam(arguments, "includeProperties"))
            },
            cancellationToken).ConfigureAwait(false));

        return response.Clone();
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
}
