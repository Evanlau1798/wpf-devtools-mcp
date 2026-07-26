using WpfDevTools.Mcp.Server.Composer.Catalog;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    private static object ToCompactCatalogItem(BlockCatalogItem item)
        => new
        {
            item.PackId,
            item.PackVersion,
            item.Kind,
            item.DisplayName,
            item.Description,
            item.Category,
            propertyNames = item.Properties.Keys.Order(StringComparer.Ordinal).ToArray(),
            propertyContracts = item.Properties
                .Where(pair => pair.Value.Required || pair.Value.Minimum.HasValue || pair.Value.Maximum.HasValue)
                .ToDictionary(
                    pair => pair.Key,
                    pair => new
                    {
                        pair.Value.Type,
                        pair.Value.Required,
                        pair.Value.AllowedValues,
                        pair.Value.AllowedValueCount,
                        pair.Value.AllowedValuesTruncated,
                        pair.Value.Minimum,
                        pair.Value.Maximum,
                        pair.Value.Integer,
                        pair.Value.Format
                    },
                    StringComparer.Ordinal),
            propertyWarnings = item.Properties
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.PreviewWarning))
                .ToDictionary(pair => pair.Key, pair => pair.Value.PreviewWarning, StringComparer.Ordinal),
            slots = item.Slots.ToDictionary(
                pair => pair.Key,
                pair => new
                {
                    pair.Value.AllowedKinds,
                    pair.Value.MinItems,
                    pair.Value.MaxItems
                },
                StringComparer.Ordinal),
            item.RendererAvailable,
            item.CompositionSkeleton,
            item.AuthoringRoles
        };
}
