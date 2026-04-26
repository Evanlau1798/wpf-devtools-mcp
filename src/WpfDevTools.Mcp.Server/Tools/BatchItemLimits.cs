using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class BatchItemLimits
{
    internal const int MaxQueryInputItems = 100;
    internal const int MaxQueryExpandedItems = 100;
    internal const int MaxMutationItems = 100;

    internal static ToolErrorPayload CreateInvalidArgumentError(
        string parameter,
        long actualItems,
        int maxItems,
        string error,
        string hint,
        IReadOnlyDictionary<string, object?>? extraErrorData = null)
    {
        var errorData = new Dictionary<string, object?>
        {
            ["parameter"] = parameter,
            ["actualItems"] = actualItems,
            ["maxItems"] = maxItems
        };

        if (extraErrorData is not null)
        {
            foreach (var item in extraErrorData)
            {
                errorData[item.Key] = item.Value;
            }
        }

        return new ToolErrorPayload
        {
            Error = error,
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = hint,
            ErrorData = errorData
        };
    }
}
