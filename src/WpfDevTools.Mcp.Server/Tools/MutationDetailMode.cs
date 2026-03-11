using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

public enum MutationDetailMode
{
    Standard,
    Compact
}

public static class MutationDetailModeParser
{
    public static (MutationDetailMode mode, object? error) Parse(JsonElement? arguments)
    {
        var detail = ParameterParser.ParseStringParam(arguments, "detail");
        if (string.IsNullOrWhiteSpace(detail))
        {
            return (MutationDetailMode.Standard, null);
        }

        return detail.Trim().ToLowerInvariant() switch
        {
            "standard" => (MutationDetailMode.Standard, null),
            "compact" => (MutationDetailMode.Compact, null),
            _ => (MutationDetailMode.Standard, new ToolErrorPayload
            {
                Error = "detail must be either 'standard' or 'compact'",
                ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
                Hint = "Use detail='standard' for full metadata or detail='compact' for a trimmed response."
            })
        };
    }
}
