namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerPackKindResolver
{
    public static string? ResolveDeclaredPackId(string blockKind, IEnumerable<string> packIds)
    {
        string? best = null;
        foreach (var packId in packIds)
        {
            if (blockKind.StartsWith(packId + ".", StringComparison.Ordinal)
                && (best is null || packId.Length > best.Length))
            {
                best = packId;
            }
        }

        return best;
    }

    public static string GetFallbackPackId(string blockKind)
    {
        var index = blockKind.IndexOf('.', StringComparison.Ordinal);
        return index < 0 ? string.Empty : blockKind[..index];
    }
}
