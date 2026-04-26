using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class BatchMutateTool
{
    private static bool TryValidateMutationCount(JsonElement mutationsElement, out object? error)
    {
        var mutationCount = mutationsElement.GetArrayLength();
        if (mutationCount <= BatchItemLimits.MaxMutationItems)
        {
            error = null;
            return true;
        }

        error = BatchItemLimits.CreateInvalidArgumentError(
            "mutations",
            mutationCount,
            BatchItemLimits.MaxMutationItems,
            $"mutations must contain at most {BatchItemLimits.MaxMutationItems} items; received {mutationCount}.",
            $"Split large mutation plans into smaller batches of {BatchItemLimits.MaxMutationItems} or fewer steps.");
        return false;
    }
}
