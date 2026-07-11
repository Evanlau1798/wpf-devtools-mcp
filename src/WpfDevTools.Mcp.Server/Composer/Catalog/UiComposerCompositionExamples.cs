using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Catalog;

internal static class UiComposerCompositionExamples
{
    private static readonly JsonElement MultipleCardsFragment = JsonSerializer.Deserialize<JsonElement>("""
        {
          "kind": "stack",
          "slots": {
            "stack": [
              {
                "kind": "wpfui.card",
                "slots": {
                  "content": [
                    {
                      "kind": "stack",
                      "slots": {
                        "stack": [
                          { "kind": "wpfui.textBlock", "properties": { "text": "First card" } }
                        ]
                      }
                    }
                  ]
                }
              },
              {
                "kind": "wpfui.card",
                "slots": {
                  "content": [
                    {
                      "kind": "stack",
                      "slots": {
                        "stack": [
                          { "kind": "wpfui.textBlock", "properties": { "text": "Second card" } }
                        ]
                      }
                    }
                  ]
                }
              }
            ]
          }
        }
        """);

    internal static object[] ForResolvedComposableItems(IReadOnlyCollection<BlockCatalogItem> items)
    {
        var cards = items.Where(item => item.Kind == "wpfui.card").Take(2).ToArray();
        var textBlocks = items.Where(item => item.Kind == "wpfui.textBlock").Take(2).ToArray();
        if (cards.Length != 1 || textBlocks.Length != 1)
        {
            return [];
        }

        var card = cards[0];
        var textBlock = textBlocks[0];
        if (!string.Equals(card.PackVersion, textBlock.PackVersion, StringComparison.Ordinal)
            || !card.Slots.TryGetValue("content", out var contentSlot)
            || !contentSlot.AllowedKinds.Contains("stack", StringComparer.Ordinal))
        {
            return [];
        }

        return
        [
            new
            {
                id = "core.stack.multiple-cards",
                purpose = "Place multiple ordered pack blocks in a slot that accepts the core stack kind.",
                placementGuidance = "Use this directly renderable fragment as a slot child, then replace the sample text with application content.",
                placementMode = "slotChild",
                compatibleParentSlots = new[] { "wpfui.card.content" },
                fragment = MultipleCardsFragment,
                wrapperBlueprint = CreateWrapperBlueprint(card.PackVersion)
            }
        ];
    }

    private static JsonElement CreateWrapperBlueprint(string packVersion)
        => JsonSerializer.SerializeToElement(new
        {
            schemaVersion = "wpfdevtools.ui-blueprint.v1",
            name = "CatalogCompositionExample",
            packs = new[]
            {
                new { id = "wpfui", version = packVersion, required = true, role = "primary" }
            },
            primaryPack = "wpfui",
            layout = new
            {
                kind = "wpfui.card",
                slots = new { content = new[] { MultipleCardsFragment } }
            }
        });
}
