using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

public enum MutationDetailMode
{
    Standard,
    Compact,
    Minimal
}

public static class MutationDetailModeParser
{
    public static (MutationDetailMode mode, object? error) Parse(JsonElement? arguments)
    {
        var detail = ParameterParser.ParseStringParam(arguments, "detail");
        if (string.IsNullOrWhiteSpace(detail))
        {
            return (MutationDetailMode.Compact, null);
        }

        return detail.Trim().ToLowerInvariant() switch
        {
            "verbose" => (MutationDetailMode.Standard, null),
            "standard" => (MutationDetailMode.Standard, null),
            "compact" => (MutationDetailMode.Compact, null),
            "minimal" => (MutationDetailMode.Minimal, null),
            _ => (MutationDetailMode.Compact, new ToolErrorPayload
            {
                Error = "detail must be 'compact', 'minimal', 'verbose', or legacy alias 'standard'",
                ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
                Hint = "Use detail='compact' for the default trimmed response, detail='minimal' for success-only mutation confirmation, detail='verbose' for full additive metadata, or detail='standard' as a compatibility alias."
            })
        };
    }
}
