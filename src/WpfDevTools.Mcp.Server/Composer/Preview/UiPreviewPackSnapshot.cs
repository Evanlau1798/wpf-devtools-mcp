using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed record UiPreviewPackSnapshot(
    PackRegistryItem RegistryItem,
    ComposerPack Pack)
{
    internal static bool TryCreate(
        PackRegistryItem discovered,
        out UiPreviewPackSnapshot? snapshot,
        out string? error)
    {
        try
        {
            var loaded = ComposerPackLoader.LoadWithFingerprint(discovered.RootPath);
            if (!string.Equals(discovered.Fingerprint, loaded.Fingerprint, StringComparison.Ordinal))
            {
                snapshot = null;
                error = $"Pack '{discovered.Id}@{discovered.Version}' changed after discovery; retry preview to review its new exact-content token.";
                return false;
            }

            snapshot = new UiPreviewPackSnapshot(discovered, loaded.Pack);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            snapshot = null;
            error = $"Pack '{discovered.Id}@{discovered.Version}' could not be loaded as one stable content snapshot: {ex.Message}";
            return false;
        }
    }
}
