namespace WpfDevTools.Mcp.Server.Composer;

internal static class ComposerPerformanceTargets
{
    public static TimeSpan PackRegistryLoad => TimeSpan.FromSeconds(2);
    public static TimeSpan BlockCatalogQuery => TimeSpan.FromSeconds(2);
    public static TimeSpan BlueprintValidation => TimeSpan.FromSeconds(2);
    public static TimeSpan RendererDryRun => TimeSpan.FromSeconds(2);
    public static TimeSpan PreviewSmoke => TimeSpan.FromSeconds(30);

    public const int MaxBlueprintNodeCount = 250;
}
